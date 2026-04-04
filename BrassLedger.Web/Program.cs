using BrassLedger.Infrastructure.Auth;
using BrassLedger.Infrastructure.Persistence;
using BrassLedger.Infrastructure.Security;
using BrassLedger.Web.Components;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddBrassLedgerCookieAuthentication();
builder.Services.AddBrassLedgerInfrastructure(builder.Configuration, builder.Environment.ContentRootPath, builder.Environment.IsDevelopment());

var app = builder.Build();
await app.Services.InitializeBrassLedgerAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

var contentTypeProvider = new FileExtensionContentTypeProvider();
var webRootPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot");

app.UseBrassLedgerSecurityHeaders();
app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
    {
        var requestedPath = context.Request.Path.Value;
        if (!string.IsNullOrWhiteSpace(requestedPath) && Path.HasExtension(requestedPath))
        {
            var relativePath = requestedPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var physicalPath = Path.GetFullPath(Path.Combine(webRootPath, relativePath));

            if (physicalPath.StartsWith(webRootPath, StringComparison.OrdinalIgnoreCase) && File.Exists(physicalPath))
            {
                if (!contentTypeProvider.TryGetContentType(physicalPath, out var contentType))
                {
                    contentType = "application/octet-stream";
                }

                context.Response.ContentType = contentType;
                await context.Response.SendFileAsync(physicalPath);
                return;
            }
        }
    }

    await next();
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapBrassLedgerAuthenticationEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
