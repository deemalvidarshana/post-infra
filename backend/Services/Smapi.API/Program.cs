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
builder.Services.AddHttpClient<IYtDlpVideoDownloader, YtDlpVideoDownloader>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});
builder.Services.AddScoped<ILocalVideoStorageService, LocalVideoStorageService>();
builder.Services.AddScoped<IVideoFrameExtractor, FfmpegVideoFrameExtractor>();
builder.Services.AddHttpClient<IGeminiCaptionGenerator, GeminiCaptionGenerator>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddHttpClient<IFacebookCommentReplyGenerator, FacebookCommentReplyGenerator>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddHttpClient<IFacebookCommentsPublisher, FacebookCommentsPublisher>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<IFacebookWebhookReceiver, FacebookWebhookReceiver>();
builder.Services.AddScoped<IFacebookCommentReplyProcessor, FacebookCommentReplyProcessor>();
builder.Services.AddHostedService<FacebookReelUploadWorker>();
builder.Services.AddHostedService<PublishedReelLocalVideoCleanupWorker>();
builder.Services.AddHostedService<FacebookPostS3UploadWorker>();
builder.Services.AddHostedService<FacebookCommentReplyWorker>();

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
