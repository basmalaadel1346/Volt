using AssessmentDA.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using AssessmentBL.Interfaces;
using AssessmentBL.Services;

namespace AssessmentBL
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddAssessmentModule(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<AssessmentDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

            services.AddScoped<IQuizAttemptService, QuizAttemptService>();
            services.AddScoped<IUserTopicStatService, UserTopicStatService>();
            services.AddScoped<IQuizService, QuizService>();
            services.AddScoped<IQuestionServiceForAdmin, QuestionService>();
            services.AddScoped<IQuestionOptionServiceForAdmin, QuestionOptionService>();

            return services;
        }
    }
}
