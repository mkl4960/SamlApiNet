using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Transforms.Builder;
using Sustainsys.Saml2;
using Sustainsys.Saml2.Saml2P;
using Sustainsys.Saml2.Configuration;
using Sustainsys.Saml2.Metadata;
using System.Security.Cryptography.X509Certificates;
using System.Text;

// Example configuration (replace with your actual values)
var spOptions = new SPOptions
{
    EntityId = new EntityId("https://your-app.example.com")
};
// Create Options with the required SPOptions parameter
var saml2Options = new Options(spOptions);
// Add your service provider certificates if needed
// Load the PFX using the recommended loader API instead of the obsolete constructor.
var serviceCert = X509CertificateLoader.LoadPkcs12FromFile("/Users/mlau/Documents/code/SamlApiNet/saml-sp.pfx", "your-cert-password");
saml2Options.SPOptions.ServiceCertificates.Add(serviceCert);

// Add your IdP's signing certificate for signature validation
saml2Options.IdentityProviders.Add(new IdentityProvider(
    new EntityId("https://idp.example.com/metadata"), saml2Options.SPOptions)
{
    SingleSignOnServiceUrl = new Uri("https://idp.example.com/sso")
    // Signing keys will be loaded from the IdP metadata by default; to add explicit certificates,
    // load them via the library's configuration APIs or from the SPOptions/IdentityProvider configuration.
});


var builder = WebApplication.CreateBuilder(args);
// Add YARP reverse proxy from config
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();


var app = builder.Build();


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}


// Middleware to modify headers before proxying
app.Use(async (context, next) =>
{
    // Example: Set or modify a header
    context.Request.Headers["X-My-Header"] = "MyValue";
    // Example: Remove a header
    context.Request.Headers.Remove("X-Remove-This");
    await next();
});

// Add YARP reverse proxy middleware
app.MapReverseProxy();

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching", "rainy", "windy", "snowy"
};

app.MapGet("/weatherforecast", (HttpContext context) =>
{
    var userAgent = context.Request.Headers["User-Agent"].ToString();

    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");



app.MapPost("/SamlService", async (HttpRequest request) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Content-Type must be application/x-www-form-urlencoded");

    var form = await request.ReadFormAsync();
    var samlResponseBase64 = form["SAMLResponse"].FirstOrDefault();

    if (string.IsNullOrEmpty(samlResponseBase64))
        return Results.BadRequest("Missing SAMLResponse parameter");

    byte[] samlBytes;
    try
    {
        samlBytes = Convert.FromBase64String(samlResponseBase64);
    }
    catch
    {
        return Results.BadRequest("Invalid base64 SAMLResponse");
    }

    string samlXml = Encoding.UTF8.GetString(samlBytes);

    // Use the static Read method with Options
    Saml2Response saml2Response;
    try
    {
        saml2Response = Saml2Response.Read(samlXml, null, saml2Options);

        // Validate signature (throws if invalid)
        var claims = saml2Response.GetClaims(saml2Options);

        // Decrypt assertions if needed (handled internally by GetClaims)
        var assertionXml = saml2Response.XmlElement.OuterXml ?? "No assertion found";
        return Results.Ok(assertionXml);
    }
    catch (Exception ex)
    {
        return Results.BadRequest($"Error processing SAMLResponse: {ex.Message}");
    }
});

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
