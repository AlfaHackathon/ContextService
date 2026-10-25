using AlfaHackaton.DapperContext;
using ContextService.Extensions;
using ContextService.Middleware;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1024 * 1024);
builder.Services.AddContextServices(builder.Configuration, builder.Environment);
builder.Services.AddServiceSwagger();
var app = builder.Build();
app.Services.UseMigrations();
app.UseContextRequestHandling();
app.UseServiceSwagger();
app.MapControllers();
app.Run();

public partial class Program;
