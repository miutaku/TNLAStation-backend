using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TNLAStation.Application.Abstractions;
using TNLAStation.Domain;

namespace TNLAStation.Api.Tests;

/// <summary>
/// EPGStation の recorded delete は、録画中の項目でも受け付けて先に停止してから消す —
/// 画面の削除ボタンは録画中・録画済みの両方をこの 1 本で扱う。実データベースを使わずに、
/// endpoint が「録画中なら IRecordingStopService を呼んでから IRecordedItemRepository.DeleteAsync
/// を呼ぶ」という順序を守っていることを、差し替えた fake で確かめる。
/// </summary>
public sealed class DeleteRecordedStopsActiveRecordingTests : IDisposable
{
    private readonly WebApplicationFactory<Program> factory;

    public DeleteRecordedStopsActiveRecordingTests()
    {
        factory = new WebApplicationFactory<Program>();
    }

    [Fact]
    public async Task DeletingARecordingInProgressStopsItFirstThenDeletes()
    {
        var calls = new List<string>();
        var repository = new FakeRecordedItemRepository(isRecording: true, calls);
        var stopService = new FakeRecordingStopService(calls);

        using WebApplicationFactory<Program> configured = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRecordedItemRepository>();
                services.AddSingleton<IRecordedItemRepository>(repository);
                services.RemoveAll<IRecordingStopService>();
                services.AddSingleton<IRecordingStopService>(stopService);
            }));
        using HttpClient client = configured.CreateClient();

        using HttpResponseMessage response = await client.DeleteAsync("/api/recorded/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // 停止してから消す。逆順だと、まだ書き込み中のファイルを消しにいくことになる。
        Assert.Equal(["stop", "delete"], calls);
    }

    [Fact]
    public async Task DeletingAnAlreadyFinishedRecordingDoesNotCallStop()
    {
        var calls = new List<string>();
        var repository = new FakeRecordedItemRepository(isRecording: false, calls);
        var stopService = new FakeRecordingStopService(calls);

        using WebApplicationFactory<Program> configured = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRecordedItemRepository>();
                services.AddSingleton<IRecordedItemRepository>(repository);
                services.RemoveAll<IRecordingStopService>();
                services.AddSingleton<IRecordingStopService>(stopService);
            }));
        using HttpClient client = configured.CreateClient();

        using HttpResponseMessage response = await client.DeleteAsync("/api/recorded/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["delete"], calls);
    }

    [Fact]
    public async Task DeletingAProtectedRecordingFails()
    {
        // EPGStation (RecordedManageModel.delete) はプロテクト中の録画を消させない。
        var calls = new List<string>();
        var repository = new FakeRecordedItemRepository(isRecording: false, calls, isProtected: true);
        var stopService = new FakeRecordingStopService(calls);

        using WebApplicationFactory<Program> configured = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRecordedItemRepository>();
                services.AddSingleton<IRecordedItemRepository>(repository);
                services.RemoveAll<IRecordingStopService>();
                services.AddSingleton<IRecordingStopService>(stopService);
            }));
        using HttpClient client = configured.CreateClient();

        using HttpResponseMessage response = await client.DeleteAsync("/api/recorded/1");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task DeletingARecordingThatDoesNotExistFails()
    {
        var calls = new List<string>();
        var repository = new FakeRecordedItemRepository(isRecording: false, calls, exists: false);
        var stopService = new FakeRecordingStopService(calls);

        using WebApplicationFactory<Program> configured = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRecordedItemRepository>();
                services.AddSingleton<IRecordedItemRepository>(repository);
                services.RemoveAll<IRecordingStopService>();
                services.AddSingleton<IRecordingStopService>(stopService);
            }));
        using HttpClient client = configured.CreateClient();

        using HttpResponseMessage response = await client.DeleteAsync("/api/recorded/999999");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(calls);
    }

    public void Dispose() => factory.Dispose();

    private sealed class FakeRecordedItemRepository(bool isRecording, List<string> calls, bool isProtected = false, bool exists = true)
        : IRecordedItemRepository
    {
        public ValueTask<RecordedProgram?> GetAsync(long recordedId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<RecordedProgram?>(!exists ? null : new RecordedProgram(
                Id: recordedId,
                ChannelId: 1,
                StartAt: 0,
                EndAt: 0,
                Name: "録画中の番組",
                HalfWidthName: "録画中の番組",
                IsRecording: isRecording,
                IsEncoding: false,
                IsProtected: isProtected));

        public ValueTask<bool> DeleteAsync(long recordedId, CancellationToken cancellationToken)
        {
            lock (calls)
            {
                calls.Add("delete");
            }

            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> SetProtectedAsync(long recordedId, bool isProtected, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<RecordedCleanupResult> CleanupAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRecordingStopService(List<string> calls) : IRecordingStopService
    {
        public ValueTask<bool> StopAsync(long recordedId, CancellationToken cancellationToken)
        {
            lock (calls)
            {
                calls.Add("stop");
            }

            return ValueTask.FromResult(true);
        }
    }
}
