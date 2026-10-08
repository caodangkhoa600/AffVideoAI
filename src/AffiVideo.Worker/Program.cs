using AffiVideo.Infrastructure;
using AffiVideo.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<Heartbeat>();

builder.Build().Run();
