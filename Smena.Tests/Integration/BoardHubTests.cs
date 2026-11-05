using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Smena.Application.Auth;
using Smena.Application.EightySix;

namespace Smena.Tests.Integration;

[Collection(ApiCollection.Name)]
public class BoardHubTests(ApiFactory factory)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    /// <summary>Povezan ekran: sve što stigne uz događaje završava u kanalima, pa test čeka poruku (ili njen izostanak).</summary>
    private sealed class Screen(HubConnection connection) : IAsyncDisposable
    {
        public Channel<EightySixItemDto> Added { get; } = Channel.CreateUnbounded<EightySixItemDto>();
        public Channel<Guid> Removed { get; } = Channel.CreateUnbounded<Guid>();
        public HubConnection Connection => connection;

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private async Task<Screen> ConnectAsync(string? token)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/board"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (token is not null)
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()))
            .Build();

        var screen = new Screen(connection);
        connection.On<EightySixItemDto>("eightySixAdded", item => screen.Added.Writer.TryWrite(item));
        connection.On<Guid>("eightySixRemoved", id => screen.Removed.Writer.TryWrite(id));
        await connection.StartAsync();
        return screen;
    }

    private static async Task<T?> NextAsync<T>(Channel<T> channel)
    {
        using var timeout = new CancellationTokenSource(Wait);
        try
        {
            return await channel.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return default;
        }
    }

    private async Task<(ApiClient Owner, ApiClient Cook, ApiClient Waiter, Guid KitchenId)> ArrangeAsync()
    {
        var owner = await ApiClient.RegisterOwnerAsync(factory);
        var cook = await owner.InviteAsync(factory);
        var waiter = await owner.InviteAsync(factory);
        var positions = await ApiClient.ReadAsync<List<PositionDto>>(await owner.Http.GetAsync("/api/positions"));
        var kitchen = positions.Single(p => p.Name == "Kuvar").Id;
        await owner.SetPositionsAsync((await cook.MeAsync()).EmployeeId, kitchen);
        await owner.SetPositionsAsync((await waiter.MeAsync()).EmployeeId, positions.Single(p => p.Name == "Konobar").Id);
        return (owner, cook, waiter, kitchen);
    }

    private static Task<HttpResponseMessage> Add(ApiClient client, Guid positionId, string name) =>
        client.Http.PostAsJsonAsync("/api/eighty-six", new { positionId, name }, ApiClient.Json);

    [Fact]
    public async Task A_new_item_reaches_every_connected_screen_of_the_restaurant()
    {
        var (owner, cook, waiter, kitchen) = await ArrangeAsync();
        await using var waiterScreen = await ConnectAsync(waiter.AccessToken);
        await using var ownerScreen = await ConnectAsync(owner.AccessToken);

        await Add(cook, kitchen, "Brancin");

        foreach (var screen in new[] { waiterScreen, ownerScreen })
        {
            var item = await NextAsync(screen.Added);
            Assert.NotNull(item);
            Assert.Equal("Brancin", item.Name);
            Assert.Equal("Kuvar", item.PositionName);
        }
    }

    [Fact]
    public async Task Taking_an_item_off_the_list_reaches_the_screens_too()
    {
        var (_, cook, waiter, kitchen) = await ArrangeAsync();
        var item = await ApiClient.ReadAsync<EightySixItemDto>(await Add(cook, kitchen, "Brancin"));
        await using var screen = await ConnectAsync(waiter.AccessToken);

        await cook.Http.DeleteAsync($"/api/eighty-six/{item.Id}");

        Assert.Equal(item.Id, await NextAsync(screen.Removed));
    }

    [Fact]
    public async Task Screens_of_another_restaurant_hear_nothing()
    {
        var (_, cook, _, kitchen) = await ArrangeAsync();
        var stranger = await ApiClient.RegisterOwnerAsync(factory);
        await using var strangerScreen = await ConnectAsync(stranger.AccessToken);

        await Add(cook, kitchen, "Brancin");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await strangerScreen.Added.Reader.ReadAsync(timeout.Token));
    }

    [Fact]
    public async Task A_refused_change_sends_nothing()
    {
        var (_, _, waiter, kitchen) = await ArrangeAsync();
        await using var screen = await ConnectAsync(waiter.AccessToken);

        var response = await Add(waiter, kitchen, "Brancin"); // konobar ne sme

        Assert.False(response.IsSuccessStatusCode);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await screen.Added.Reader.ReadAsync(timeout.Token));
    }

    [Fact]
    public async Task Connecting_needs_a_valid_token()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => ConnectAsync(null));
        await Assert.ThrowsAnyAsync<Exception>(() => ConnectAsync("nije-token"));
    }
}
