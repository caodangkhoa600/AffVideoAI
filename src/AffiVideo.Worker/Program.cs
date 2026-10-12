using AffiVideo.Application.Providers;
using AffiVideo.Domain;
using AffiVideo.Infrastructure;
using AffiVideo.Worker;
using AffiVideo.Worker.Generation;
using AffiVideo.Worker.Rendering;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddRenderQueue(builder.Configuration);
builder.Services.AddHostedService<Heartbeat>();

builder.Services.Configure<RenderingOptions>(builder.Configuration.GetSection(RenderingOptions.Section));
// The cut-out runs here, in the worker, and nowhere else: the model is only in the worker image.
builder.Services.AddSingleton<IImageProcessor, OnnxCutOut>();
builder.Services.AddSingleton<Remotion>();
builder.Services.AddSingleton<Ffmpeg>();
builder.Services.AddScoped<VideoLayers>();
builder.Services.AddScoped<SceneClips>();
builder.Services.AddScoped<RenderJobRunner>();
builder.Services.AddHostedService<RenderWorker>();
builder.Services.AddHostedService<StillWorker>();
// Whatever is configured to make Clips. A real provider is registered in the fake's place, never beside it.
builder.Services.AddSingleton<IVideoGenerator>(provider =>
    provider.GetRequiredService<IOptions<GenerationOptions>>().Value switch
    {
        { VideoProvider: VideoProvider.Fake, FakeVideo: var fake } =>
            new FakeVideoGenerator(fake, provider.GetRequiredService<IOptions<RenderingOptions>>()),
        _ => new NoVideoGenerator(),
    });
builder.Services.AddHostedService<ClipWorker>();

builder.Build().Run();
