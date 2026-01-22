using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.OpenApi.Models;
using UnibouwAPI.Repositories;
using UnibouwAPI.Repositories.Interfaces;
using UnibouwAPI.Service;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Add controllers
builder.Services.AddControllers();

// Configure logging
/*builder.Logging.ClearProviders();
builder.Logging.AddDebug();
builder.Logging.AddConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "[HH:mm:ss] ";
});*/
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration.ReadFrom.Configuration(context.Configuration);
});

// Register Repositories
builder.Services.AddScoped<ICommon, CommonRepository>();
builder.Services.AddScoped<IWorkItems, WorkItemsRepository>();
builder.Services.AddScoped<ISubcontractors, SubcontractorsRepository>();
builder.Services.AddScoped<IProjects, ProjectsRepository>();
builder.Services.AddScoped<IRfq, RfqRepository>();
builder.Services.AddScoped<IEmail, EmailRepository>();
builder.Services.AddScoped<IRfqResponse, RfqResponseRepository>();
builder.Services.AddScoped<IRFQConversationMessage, RFQConversationMessageRepository>();
builder.Services.AddScoped<IMsTeamsNotification, MsTeamsNotificationService>();
builder.Services.AddScoped<IRfqReminderSet, RfqReminderSetRepository>();


// Configure Azure AD authentication with custom Unauthorized/Forbidden responses
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddInMemoryTokenCaches();

builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.Events = new JwtBearerEvents
    {
        OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            var result = System.Text.Json.JsonSerializer.Serialize(new
            {
                statusCode = 401,
                errorCode = "UNAUTHORIZED",
                message = "Not Authorized"
            });
            return context.Response.WriteAsync(result);
        },
        OnForbidden = context =>
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            var result = System.Text.Json.JsonSerializer.Serialize(new
            {
                statusCode = 403,
                errorCode = "FORBIDDEN",
                message = "You do not have permission to access this resource."
            });
            return context.Response.WriteAsync(result);
        }
    };
});

// Authorization policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ReadPolicy", policy => policy.RequireRole("Admin", "ProjectManager"));
    options.AddPolicy("WritePolicy", policy => policy.RequireRole("Admin"));
});

// Configure Swagger with OAuth2
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Unibouw API", Version = "v1" });

    // OAuth2 definition
    c.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.OAuth2,
        Flows = new OpenApiOAuthFlows
        {
            AuthorizationCode = new OpenApiOAuthFlow
            {
                AuthorizationUrl = new Uri($"{builder.Configuration["AzureAd:Instance"]}{builder.Configuration["AzureAd:TenantId"]}/oauth2/v2.0/authorize"),
                TokenUrl = new Uri($"{builder.Configuration["AzureAd:Instance"]}{builder.Configuration["AzureAd:TenantId"]}/oauth2/v2.0/token"),
                Scopes = new Dictionary<string, string>
                {
                    { $"api://{builder.Configuration["AzureAd:ClientId"]}/Api.Read", "Read access to API" },
                    { $"api://{builder.Configuration["AzureAd:ClientId"]}/Api.Write", "Write access to API" }
                }
            }
        }
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "oauth2" }
            },
            new[] {
                $"api://{builder.Configuration["AzureAd:ClientId"]}/Api.Read",
                $"api://{builder.Configuration["AzureAd:ClientId"]}/Api.Write"
            }
        }
    });
});

// CORS setup based on environment
string[] allowedOrigins = builder.Environment.EnvironmentName switch
{
    "Development" => new[]
    {
        "http://localhost:4200",
        "https://unibouwqa.flatworldinfotech.com"
    },
    "QA" => new[] { "https://unibouwqa.flatworldinfotech.com", "http://localhost:4200" },
    "UAT" => new[] { "https://unibouwuat.flatworldinfotech.com" },
    "Production" => new[] { "https://unibouw.flatworldinfotech.com" },
    _ => Array.Empty<string>()
};

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Enable Swagger in all environments (optional)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Unibouw API V1");
    c.OAuthClientId(builder.Configuration["AzureAd:SwaggerClientID"]);
    c.OAuthAppName("Unibouw API - Swagger");
    c.OAuthUsePkce();
});

// Developer exception page in Development only
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();

// CORS must come before authentication
app.UseCors("AllowSpecificOrigins");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

Console.WriteLine($"------------ App starting in environment: {app.Environment.EnvironmentName}");

app.Run();
