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

            // Initialize DatabaseConnection singleton
            DatabaseConnection.Initialize(builder.Configuration);

            // Add services to the container.
            builder.Services.AddControllers();
            builder.Services.AddOpenApi();

            // DI: domain/application wiring
            builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
            builder.Services.AddScoped<ISaleService, Sale.Application.Services.SaleService>();

            // Messaging / outbox / idempotency registrations
            builder.Services.AddSingleton<IEventPublisher, RabbitPublisher>();

            // Register OutboxRepository for background processor as transient using a plain connection (no transaction)
            builder.Services.AddTransient<IOutboxRepository>(sp =>
                new OutboxRepository(DatabaseConnection.Instance.GetConnection(), null));

            // Idempotency repository can be singleton (it manages its own connections)
            builder.Services.AddSingleton<IIdempotencyStore, IdempotencyRepository>();

            // Hosted services
            builder.Services.AddHostedService<OutboxProcessor>();
            builder.Services.AddHostedService<RabbitConsumer>();

            // 1. CONFIGURACIÓN JWT
            var jwtSettings = builder.Configuration.GetSection("JwtSettings");
            var secretKey = jwtSettings.GetValue<string>("SecretKey");
            var issuer = jwtSettings.GetValue<string>("Issuer");
            var audience = jwtSettings.GetValue<string>("Audience");

            var keyBytes = Encoding.UTF8.GetBytes(secretKey!);

            builder.Services.AddAuthentication(config => {
                config.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                config.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(config => {
                config.RequireHttpsMetadata = false; // Pon en true para Producción
                config.SaveToken = true;
                config.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    ClockSkew = TimeSpan.Zero // Para que el token expire exactamente cuando dice
                };
            });

            builder.Services.AddControllers();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.MapOpenApi();
            }

            app.UseHttpsRedirection();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            app.Run();
        }
    }
}
