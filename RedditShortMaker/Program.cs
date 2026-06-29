using RedditShortMaker.Components;
using RedditShortMaker.Models;
using RedditShortMaker.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient();
builder.Services.AddSingleton<RedditService>();
builder.Services.AddSingleton<RedditCardService>();
builder.Services.AddSingleton<EdgeTtsService>();
builder.Services.AddSingleton<SubtitleGeneratorService>();
builder.Services.AddSingleton<FfmpegService>();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(exceptionHandlerApp =>
    {
        exceptionHandlerApp.Run(async context =>
        {
            var feature = context.Features.Get<IExceptionHandlerPathFeature>();
            var exception = feature?.Error;

            int status = exception switch
            {
                RedditPostNotFoundException => 404,
                RedditFetchException => 502,
                _ => 500
            };
            string title = exception switch
            {
                RedditPostNotFoundException => exception.Message,
                RedditFetchException => exception.Message,
                _ => "An error occurred."
            };

            context.Response.StatusCode = status;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = status,
                Title = title,
                Type = $"https://httpstatuses.com/{status}",
            });
        });
    });
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(Directory.GetCurrentDirectory(), "outputs")),
    RequestPath = "/outputs",
});
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
