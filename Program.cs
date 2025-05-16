using GFAS.Email;
using GFAS.Models;
using GFAS.PasswordHasher;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllersWithViews();
var Provider = builder.Services.BuildServiceProvider();
var Config = Provider.GetRequiredService<IConfiguration>();
builder.Services.AddDbContext<INNOVATIONDBContext>(item => item.UseSqlServer(Config.GetConnectionString("LoginDB")));
builder.Services.AddDbContext<UserLoginDBContext>(item => item.UseSqlServer(Config.GetConnectionString("UserLoginDB")));
builder.Services.AddDbContext<TSUISLRFIDDBContext>(item => item.UseSqlServer(Config.GetConnectionString("RFID")));

builder.Services.AddTransient<Hash_Password>();
builder.Services.AddTransient<EmailService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication("Cookies")
.AddCookie("Cookies", options =>
{
    options.LoginPath = "/User/Login";
    options.ExpireTimeSpan = TimeSpan.FromDays(1);
    options.SlidingExpiration = true;
});
builder.Services.AddAuthorization();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromDays(1);
    options.Cookie.HttpOnly = false;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();


if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
   
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    if (!context.User.Identity.IsAuthenticated)
    {
        var userId = context.Request.Cookies["Session"];
        var UserName = context.Request.Cookies["UserName"];
        var Pno = context.Request.Cookies["UserSession"];
        if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(UserName) && !string.IsNullOrEmpty(Pno))
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name,UserName),
                new Claim("Pno",Pno),
                new Claim("Session",userId)
            };

            var identity = new ClaimsIdentity(claims, "Cookies");
            var principal = new ClaimsPrincipal(identity);
            context.User = principal;

        }
    }
        await next();
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseSession();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Geo}/{action=GeoFencing}/{id?}");

app.Run();
