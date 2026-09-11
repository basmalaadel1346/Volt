using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Assessment.AI;

namespace AIIntegration;

public static class DependencyInjection
{
    public static IServiceCollection AddAiIntegration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AiSettings>(configuration.GetSection(AiSettings.SectionName));
        services.AddHttpClient<IExternalAiProvider, HttpExternalAiProvider>();
        services.AddScoped<IAiHintGenerator, AiHintGenerator>();
        return services;
    }
}
