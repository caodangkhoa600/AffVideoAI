using AffiVideo.Application.Providers;
using AffiVideo.Infrastructure;
using AffiVideo.Worker;
using AffiVideo.Worker.Rendering;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddRenderQueue();
builder.Services.AddHostedService<Heartbeat>();

builder.Services.Configure<RenderingOptions>(builder.Configuration.GetSection(RenderingOptions.Section));
// The cut-out runs here, in the worker, and nowhere else: the model is only in the worker image.
builder.Services.AddSingleton<IImageProcessor, OnnxCutOut>();
builder.Services.AddSingleton<Remotion>();
builder.Services.AddSingleton<Ffmpeg>();
builder.Services.AddScoped<VideoLayers>();
builder.Services.AddScoped<RenderJobRunner>();
builder.Services.AddHostedService<RenderWorker>();

builder.Build().Run();
