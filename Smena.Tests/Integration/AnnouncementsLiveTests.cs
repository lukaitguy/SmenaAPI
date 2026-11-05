using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Smena.Application.Announcements;
using Smena.Application.Auth;

namespace Smena.Tests.Integration;

/// <summary>Najave stižu uživo samo onima kojima su namenjene; menadžeri dobijaju signale o spisku i čitanju.</summary>
[Collection(ApiCollection.Name)]
public class AnnouncementsLiveTests(ApiFactory factory)
{
    private sealed class Screen(HubConnection connection) : IAsyncDisposable
    {
        public Channel<AnnouncementDto> Created { get; } = Channel.CreateUnbounded<AnnouncementDto>();
        public Channel<Guid> Deleted { get; } = Channel.CreateUnbounded<Guid>();
        public Channel<bool> Changed { get; } = Channel.CreateUnbounded<bool>();
        public Channel<JsonElement> Read { get; } = Channel.CreateUnbounded<JsonElement>();

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private async Task<Screen> ConnectAsync(ApiClient client)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/board"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(client.AccessToken);
            })
            .Build();

        var screen = new Screen(connection);
        connection.On<AnnouncementDto>("announcementCreated", item => screen.Created.Writer.TryWrite(item));
        connection.On<Guid>("announcementDeleted", id => screen.Deleted.Writer.TryWrite(id));
        connection.On("announcementsChanged", () => screen.Changed.Writer.TryWrite(true));
        connection.On<JsonElement>("announcementRead", payload => screen.Read.Writer.TryWrite(payload));
        await connection.StartAsync();
        return screen;
    }

    private static async Task<T?> NextAsync<T>(Channel<T> channel)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            return await channel.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return default;
        }
    }

    private static async Task AssertSilentAsync<T>(Channel<T> channel)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await channel.Reader.ReadAsync(timeout.Token));
    }

    private async Task<(ApiClient Owner, ApiClient Waiter, ApiClient Cook, Guid WaiterPosition)> ArrangeAsync()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var waiter = await owner.InviteAsync(factory);
        var cook = await owner.InviteAsync(factory);
        var positions = await ApiClient.ReadAsync<List<PositionDto>>(await owner.Http.GetAsync("/api/positions"));
        await owner.SetPositionsAsync((await waiter.MeAsync()).EmployeeId, positions.Single(p => p.Name == "Konobar").Id);
        await owner.SetPositionsAsync((await cook.MeAsync()).EmployeeId, positions.Single(p => p.Name == "Kuvar").Id);
        return (owner, waiter, cook, positions.Single(p => p.Name == "Konobar").Id);
    }

    private static Task<HttpResponseMessage> Create(ApiClient client, Guid positionId) =>
        client.Http.PostAsJsonAsync("/api/announcements",
            new { title = "Sastanak", body = "Svi", positionIds = new[] { positionId } }, ApiClient.Json);

    [Fact]
    public async Task A_new_announcement_reaches_only_its_recipients_live()
    {
        var (owner, waiter, cook, waiterPosition) = await ArrangeAsync();
        await using var waiterScreen = await ConnectAsync(waiter);
        await using var cookScreen = await ConnectAsync(cook);

        await Create(owner, waiterPosition);

        var item = await NextAsync(waiterScreen.Created);
        Assert.NotNull(item);
        Assert.Equal("Sastanak", item.Title);
        Assert.True(item.Recipients is null); // primalac ne dobija spisak ostalih
        await AssertSilentAsync(cookScreen.Created);
    }

    [Fact]
    public async Task Managers_are_told_to_refresh_when_the_list_changes()
    {
        var (owner, _, _, waiterPosition) = await ArrangeAsync();
        await using var ownerScreen = await ConnectAsync(owner);

        var created = await ApiClient.ReadAsync<AnnouncementDto>(await Create(owner, waiterPosition));
        Assert.True(await NextAsync(ownerScreen.Changed));

        await owner.Http.DeleteAsync($"/api/announcements/{created.Id}");
        Assert.True(await NextAsync(ownerScreen.Changed));
    }

    [Fact]
    public async Task Deleting_tells_the_recipients_screens()
    {
        var (owner, waiter, _, waiterPosition) = await ArrangeAsync();
        var created = await ApiClient.ReadAsync<AnnouncementDto>(await Create(owner, waiterPosition));
        await using var waiterScreen = await ConnectAsync(waiter);

        await owner.Http.DeleteAsync($"/api/announcements/{created.Id}");

        Assert.Equal(created.Id, await NextAsync(waiterScreen.Deleted));
    }

    [Fact]
    public async Task Who_has_read_it_is_pushed_to_managers_only()
    {
        var (owner, waiter, cook, waiterPosition) = await ArrangeAsync();
        var created = await ApiClient.ReadAsync<AnnouncementDto>(await Create(owner, waiterPosition));
        await using var ownerScreen = await ConnectAsync(owner);
        await using var cookScreen = await ConnectAsync(cook);
        await using var waiterScreen = await ConnectAsync(waiter);

        await waiter.Http.PostAsync($"/api/announcements/{created.Id}/read", null);

        var read = await NextAsync(ownerScreen.Read);
        Assert.Equal(created.Id, read.GetProperty("announcementId").GetGuid());
        Assert.Equal((await waiter.MeAsync()).EmployeeId, read.GetProperty("employeeId").GetGuid());
        await AssertSilentAsync(cookScreen.Read);
        await AssertSilentAsync(waiterScreen.Read);
    }

    [Fact]
    public async Task Repeating_read_pushes_nothing_new()
    {
        var (owner, waiter, _, waiterPosition) = await ArrangeAsync();
        var created = await ApiClient.ReadAsync<AnnouncementDto>(await Create(owner, waiterPosition));
        await using var ownerScreen = await ConnectAsync(owner);
        await waiter.Http.PostAsync($"/api/announcements/{created.Id}/read", null);
        await NextAsync(ownerScreen.Read);

        await waiter.Http.PostAsync($"/api/announcements/{created.Id}/read", null);

        await AssertSilentAsync(ownerScreen.Read);
    }
}
