using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Identity.Web;
using Microsoft.OpenApi.Models;
using UnibouwAPI.Repositories;
using UnibouwAPI.Repositories.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Add controllers
builder.Services.AddControllers();

//Register Unibouw Repository
builder.Services.AddScoped<IWorkItems, WorkItemsRepository>();
builder.Services.AddScoped<IWorkItemCategoryType, WorkItemCategoryTypeRepository>();

// Configure Azure AD authentication with custom Unauthorized/Forbidden responses
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddInMemoryTokenCaches();

// Customize 401 and 403 responses
builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.Events = new JwtBearerEvents
    {
        OnChallenge = context =>
        {
            context.HandleResponse(); // prevent default response
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

// Add Authorization
//builder.Services.AddAuthorization();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ReadPolicy", policy => policy.RequireRole("Admin", "ProjectManager"));
    options.AddPolicy("WritePolicy", policy => policy.RequireRole("Admin"));
});

// Configure Swagger with OAuth2
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "My API", Version = "v1" });

    // OAuth2 Definition
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
            new[] { $"api://{builder.Configuration["AzureAd:ClientId"]}/Api.Read", $"api://{builder.Configuration["AzureAd:ClientId"]}/Api.Write" }
        }
    });
});

// CORS
string[] allowedOrigins = builder.Environment.IsDevelopment()
    ? new[] { "http://localhost:4200" }              // Local Angular
    : builder.Environment.IsEnvironment("QA")
        ? new[] { "https://qa-portal.unibouw.com" } // QA
        : builder.Environment.IsEnvironment("UAT")
            ? new[] { "https://uat-portal.unibouw.com" } // UAT
            : builder.Environment.IsProduction()
                ? new[] { "https://portal.unibouw.com" } // Production
                : Array.Empty<string>();

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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
        c.OAuthClientId(builder.Configuration["AzureAd:SwaggerClientID"]);
        c.OAuthAppName("My API - Swagger");
        c.OAuthUsePkce();
    });
}

app.UseHttpsRedirection();

// Use CORS
app.UseCors("AllowSpecificOrigins");

// Important: order matters
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
