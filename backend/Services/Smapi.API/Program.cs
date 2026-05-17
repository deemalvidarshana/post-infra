using Smapi.API.Data;
using Smapi.API.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHttpClient<IApifyFacebookPostsClient, ApifyFacebookPostsClient>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddHttpClient<IApifyTikTokPostsClient, ApifyFacebookPostsClient>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddHttpClient<IFacebookReelsPublisher, FacebookReelsPublisher>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
    SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
});
builder.Services.AddSingleton<IFacebookReelUploadQueue, FacebookReelUploadQueue>();
builder.Services.AddSingleton<IFacebookPostS3UploadQueue, FacebookPostS3UploadQueue>();
builder.Services.AddSingleton<IFacebookPostS3DownloadCancellation, FacebookPostS3DownloadCancellation>();
builder.Services.AddScoped<IYtDlpVideoDownloader, YtDlpVideoDownloader>();
builder.Services.AddScoped<ILocalVideoStorageService, LocalVideoStorageService>();
builder.Services.AddScoped<IVideoFrameExtractor, FfmpegVideoFrameExtractor>();
builder.Services.AddHttpClient<IGeminiCaptionGenerator, GeminiCaptionGenerator>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddHostedService<FacebookReelUploadWorker>();
builder.Services.AddHostedService<FacebookPostS3UploadWorker>();

// Database
builder.Services.AddDbContext<SmapiDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy => policy.WithOrigins("http://localhost:3000")
                        .AllowAnyMethod()
                        .AllowAnyHeader());
});

var app = builder.Build();

// Automatic Database Migration
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<SmapiDbContext>();
        context.Database.Migrate();
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred during migration.");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors("AllowFrontend");

app.UseAuthorization();

app.MapControllers();

app.Run();
