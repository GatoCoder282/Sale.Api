using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Sale.Domain.Ports;
using Sale.Infraestructure.Data;
using Sale.Infraestructure.Messaging;
using Sale.Infraestructure.Persistence;
using System.Text;

namespace Sale.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 1. Inicializar DB
            DatabaseConnection.Initialize(builder.Configuration);

            builder.Services.AddControllers();
            builder.Services.AddOpenApi();

            // 2. Inyección de Dependencias
            builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
            builder.Services.AddScoped<ISaleService, Sale.Application.Services.SaleService>();
            builder.Services.AddSingleton<IEventPublisher, RabbitPublisher>();
            builder.Services.AddSingleton<IIdempotencyStore, IdempotencyRepository>();
            builder.Services.AddTransient<IOutboxRepository>(sp =>
                new OutboxRepository(DatabaseConnection.Instance.GetConnection(), null));

            builder.Services.AddHostedService<OutboxProcessor>();
            builder.Services.AddHostedService<RabbitConsumer>();

            // =================================================================
            // 3. CONFIGURACIÓN JWT (Sincronizada con tu JwtTokenService)
            // =================================================================

            // Usamos "Jwt" porque así está en tu appsettings.json
            var jwtSection = builder.Configuration.GetSection("Jwt");
            var secretKey = jwtSection.GetValue<string>("Key");
            var issuer = jwtSection.GetValue<string>("Issuer");
            var audience = jwtSection.GetValue<string>("Audience");

            if (string.IsNullOrEmpty(secretKey)) throw new Exception("Falta Jwt:Key en appsettings");

            // IMPORTANTE: Usamos UTF8 porque tu JwtTokenService usa Encoding.UTF8.GetBytes
            var keyBytes = Encoding.UTF8.GetBytes(secretKey);

            builder.Services.AddAuthentication(config => {
                config.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                config.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(config => {
                config.RequireHttpsMetadata = false;
                config.SaveToken = true;

                config.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),

                    ValidateIssuer = true,
                    ValidIssuer = issuer, // Debe ser "FarmaArquiSoft"

                    ValidateAudience = true,
                    ValidAudience = audience, // Debe ser "FarmaArquiSoftClients"

                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };

                // ?? ESTO ES ORO PURO: EVENTOS DE DEPURACIÓN
                // Si el token falla, esto imprimirá el error exacto en la consola de "dotnet run"
                config.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        Console.WriteLine("?? AUTH FAILED: " + context.Exception.Message);
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        Console.WriteLine("?? TOKEN VALIDATED for: " + context.Principal?.Identity?.Name);
                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        Console.WriteLine("?? CHALLENGE: " + context.Error + " - " + context.ErrorDescription);
                        return Task.CompletedTask;
                    }
                };
            });

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            app.UseHttpsRedirection();

            // 4. Activar Seguridad
            app.UseAuthentication(); // <-- Identifica al usuario
            app.UseAuthorization();  // <-- Verifica permisos

            app.MapControllers();

            app.Run();
        }
    }
}