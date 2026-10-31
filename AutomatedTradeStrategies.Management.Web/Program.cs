using AutomatedTrading.Core.Data;
using AutomatedTrading.Core.Data.Commands;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Framework.DataLayer.Sqlite;
using AutomatedTrading.Core.Models;
using AutomatedTradeStrategies.Management.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Data access — same SQLite database as AutomatedTrading.Core.Api.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
var connectionFactory = new SqliteConnectionFactory(connectionString);
builder.Services.AddSingleton<IDbConnectionFactory>(_ => connectionFactory);
builder.Services.AddScoped(typeof(IDefaultRepository<>), typeof(DefaultRepository<>));
builder.Services.AddScoped(typeof(IDefaultInsertCommand<>), typeof(DefaultInsertCommand<>));
builder.Services.AddScoped(typeof(IDefaultUpdateCommand<>), typeof(DefaultUpdateCommand<>));
builder.Services.AddScoped(typeof(IDefaultDeleteCommand<>), typeof(DefaultDeleteCommand<>));

// Overrides the generic DefaultInsertCommand<BarData> registered above: batches inserts and
// updates existing rows matching (SessionDate, SessionTime, Instrument, InstrumentVersion) instead of a plain INSERT.
builder.Services.AddScoped<IDefaultInsertCommand<BarData>, BarDataInsertCommand>();

// Auto-register every query/command class so new ones don't need to be listed here.
var queryAndCommandTypes = typeof(IDatabaseEntity).Assembly.GetTypes()
    .Where(t => t is
    {
        IsClass: true,
        IsAbstract: false,
        IsNested: false, // skips compiler-generated iterator/closure classes
        Namespace: "AutomatedTrading.Core.Data.Queries" or "AutomatedTrading.Core.Data.Commands"
    });

foreach (var type in queryAndCommandTypes)
    builder.Services.AddScoped(type);

// Singleton so background feature exports (and their status) outlive the request that started them.
builder.Services.AddSingleton<FeatureExportJobs>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
