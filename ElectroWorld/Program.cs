using AIIntegration;
using AssessmentBL;
using ContentBL;
using ElectroWorld.Middleware;
using Microsoft.AspNetCore.Mvc;
using ElectroWorld.Swagger;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Shared;
using Shared.Users;
using System.Text;
using UsersBL;

var builder = WebApplication.CreateBuilder(args);

// ---------- Services ----------
builder.Services.AddControllers(options =>
{
    // Global application/json for every response. Applied as a filter rather
    // than per-action so no endpoint can drift.
    // NOTE: ConsumesAttribute is deliberately NOT added globally — it would make
    // the multipart image-upload endpoint return 415.
    options.Filters.Add(new ProducesAttribute("application/json"));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "اكتبي التوكن هنا من غير كلمة Bearer قبله، Swagger بيضيفها لوحده. مثال: eyJhbGciOiJIUzI1NiIs..."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });

    // Filter order matters and is deliberate:
    //  1. add the error responses every endpoint can return (401/403/500),
    //  2. overlay any per-action [SwaggerExample],
    //  3. strip every media type except application/json — must run LAST so it
    //     also normalizes whatever the first two added.
    options.OperationFilter<DefaultApiResponsesOperationFilter>();
    options.OperationFilter<ResponseExamplesOperationFilter>();
    options.OperationFilter<JsonOnlyOperationFilter>();
});

builder.Services.AddShared(builder.Configuration);
builder.Services.AddUsersModule(builder.Configuration);
builder.Services.AddContentModule(builder.Configuration);
builder.Services.AddAssessmentModule(builder.Configuration);
builder.Services.AddAiIntegration(builder.Configuration);

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt section is missing from appsettings.json");

builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        // Makes 401/403 return the ApiResponse envelope instead of an empty body,
        // so the Swagger examples for those codes are truthful.
        options.Events = ApiResponseAuthWriter.Events;

        // من غير السطر ده، .NET بيحول أسماء الـ Claims القياسية (زي "sub") لأسماء تانية
        // (ClaimTypes.NameIdentifier) تلقائيًا، وده بيلخبط قراءة الـ Claims. بنسيبها زي
        // ما إحنا كتبناها بالظبط في JwtTokenGenerator.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey))
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// ---------- Pipeline ----------
//if (app.Environment.IsDevelopment())
//{
    app.UseSwagger();
    app.UseSwaggerUI();
//}

app.UseHttpsRedirection();
app.UseMiddleware<ExceptionMiddleware>();
app.UseStaticFiles(); // عشان الصور اللي جوه wwwroot/uploads تبقى قابلة للوصول من رابط مباشر

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
