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

            // =============================================================
            //
            // =============================================================
            var jwtSection = builder.Configuration.GetSection("Jwt");
            var keyConfig = jwtSection["Key"];
            var issConfig = jwtSection["Issuer"];
            var audConfig = jwtSection["Audience"];

            Console.BackgroundColor = ConsoleColor.Blue;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("\n========================================================");
            Console.WriteLine("  DIAGNÓSTICO DE ARRANQUE (SALES API)");
            Console.WriteLine("========================================================");
            Console.WriteLine($" CLAVE LEÍDA: '{keyConfig}'");
            Console.WriteLine($"   (Longitud: {keyConfig?.Length ?? 0} caracteres)");
            Console.WriteLine($" ISSUER:      '{issConfig}'");
            Console.WriteLine($" AUDIENCE:    '{audConfig}'");
            Console.WriteLine("========================================================\n");
            Console.ResetColor();

            if (string.IsNullOrWhiteSpace(keyConfig))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(" ERROR CRÍTICO: La clave JWT está vacía o nula en appsettings.json");
                Console.ResetColor();
                throw new Exception("Falta Jwt:Key en appsettings");
            }
            // =============================================================

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // 2. CORS (Permitir todo para evitar problemas de red local)
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
                });
            });

            // 3. Inyección de Dependencias
            builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
            builder.Services.AddScoped<ISaleService, Sale.Application.Services.SaleService>();
            builder.Services.AddSingleton<IEventPublisher, RabbitPublisher>();
            builder.Services.AddSingleton<IIdempotencyStore, IdempotencyRepository>();
            builder.Services.AddTransient<IOutboxRepository>(sp =>
                new OutboxRepository(DatabaseConnection.Instance.GetConnection(), null));

            // Servicios en segundo plano (RabbitMQ)
            builder.Services.AddHostedService<OutboxProcessor>();
            builder.Services.AddHostedService<RabbitConsumer>();

            // 4. Configuración JWT (Modo Permisivo para pruebas)
            var keyBytes = Encoding.UTF8.GetBytes(keyConfig);

            builder.Services.AddAuthentication(config => {
                config.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                config.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(config => {
                config.RequireHttpsMetadata = false;
                config.SaveToken = true;

                config.TokenValidationParameters = new TokenValidationParameters
                {
                    //  Solo validamos que la firma sea correcta (la clave coincida)
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),

                    //  Desactivamos lo demás para descartar errores de texto/fecha
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = false, // Acepta tokens vencidos por ahora

                    ClockSkew = TimeSpan.Zero
                };

                // Logs de error detallados
                config.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($" AUTH FAILED: {context.Exception.Message}");
                        Console.ResetColor();
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($" TOKEN VALIDO. Usuario: {context.Principal?.Identity?.Name}");
                        Console.ResetColor();
                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        // Si llegamos aquí sin pasar por OnAuthenticationFailed, es la firma.
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($" CHALLENGE (401): {context.Error} - {context.ErrorDescription}");
                        Console.ResetColor();
                        return Task.CompletedTask;
                    }
                };
            });

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            //app.UseHttpsRedirection();

            // Usar CORS antes de la autenticación
            app.UseCors("AllowAll");

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}