using System;
using System.Linq;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Hangfire;
using Hangfire.MySql;
using Microsoft.AspNetCore.Authentication.Cookies;
using Cranelift.Helpers;
using Cranelift.Services;
using Hangfire.Console;
using Cranelift.Jobs;
using System.Net;

namespace Cranelift
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHangfire(configuration => configuration
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseStorage(new MySqlStorage(Configuration.GetConnectionString(Constants.HangfireConnectionName), new MySqlStorageOptions
                {
                    QueuePollInterval = TimeSpan.FromSeconds(1),
                    InvisibilityTimeout = TimeSpan.FromMinutes(5),
                }))
                .UseConsole());

            var workerOptions = Configuration.GetSection(Constants.Worker).Get<WorkerOptions>();

            // Add the processing server as IHostedService
            services.AddHangfireServer(options =>
            {
                options.WorkerCount = workerOptions.WorkerCount;
            });

            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/Login";
                    // options.EventsType = typeof(CustomCookieAuthenticationEvents);
                });

            services.AddScoped<CustomCookieAuthenticationEvents>();

            services.AddHostedService<JobListener>();

            services.AddScoped<IDbContext, MySqlDbContext>();
            services.AddScoped<IStorage, S3Storage>();
            services.AddScoped<PythonHelper>();
            services.AddScoped<DocumentHelper>();
            services.AddScoped<FastPayService>();
            services.AddScoped<EmailSender>();

            var emailOptions = Configuration.GetSection("Email").Get<EmailOptions>();
            if (emailOptions is null)
                throw new InvalidOperationException("Please fill out email configuration.");

            services.AddFluentEmail(emailOptions.FromAddress, "Zhir")
                .AddSmtpSender(() => new System.Net.Mail.SmtpClient
                {
                    Host = emailOptions.Host,
                    Port = emailOptions.Port,
                    Credentials = new NetworkCredential(emailOptions.FromAddress, emailOptions.Password),
                    EnableSsl = true,
                });

            services.AddHttpClient();
            services.AddRazorPages();
            services.AddControllers();

            Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IBackgroundJobClient backgroundJobClient)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            var scheduledJobs = JobStorage.Current.GetMonitoringApi().ScheduledJobs(0, 100)
                .Where(j => j.Value.Job.Type == typeof(FastPayWatcherJob))
                .Select(j => j.Key)
                .ToArray();

            RecurringJob.AddOrUpdate<MonthlyGiftJob>(job => job.Execute(null), "0 0 1 * *"); // At 00:00 of 1st day of every month

            foreach (var job in scheduledJobs)
            {
                backgroundJobClient.Delete(job);
            }

            backgroundJobClient.Enqueue<FastPayWatcherJob>(job => job.Execute(null));

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseHangfireDashboard(pathMatch: "/hangfire", new DashboardOptions
            {
                Authorization = new[] { new HangfireAuthorizationFilter() }
            });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapHangfireDashboard();
                endpoints.MapControllers();
            });
        }
    }
}
