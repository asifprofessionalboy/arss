using GFAS.Email;
using GFAS.Models;
using GFAS.PasswordHasher;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NuGet.Protocol.Plugins;
using System;
using System.Net.Mail;

namespace GFAS.Controllers
{
    public class UserController : Controller
    {
        private readonly INNOVATIONDBContext context;
        private readonly Hash_Password hash_Password;
        private readonly UserLoginDBContext context1;
        private readonly EmailService emailService;

        public UserController(INNOVATIONDBContext context,Hash_Password hash_Password,UserLoginDBContext context1,EmailService emailService) 
        {
            this.context = context;
            this.hash_Password = hash_Password;
            this.context1 = context1;
            this.emailService = emailService;
        }

        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(AppLogin login)
        {

            if (!string.IsNullOrEmpty(login.UserId) && string.IsNullOrEmpty(login.Password))
            {
                ViewBag.FailedMsg = "Login Failed: Password is required";
                return View(login);
            }


            var user = await context.AppLogins
                .Where(x => x.UserId == login.UserId)
                .FirstOrDefaultAsync();

            if (user != null)
            {

                bool isPasswordValid = hash_Password.VerifyPassword(login.Password, user.Password, user.PasswordSalt);

                if (isPasswordValid)
                {
                    var UserLoginData = await context1.AppEmployeeMasters.
                        Where(x => x.Pno == login.UserId).FirstOrDefaultAsync();

                    string userName = UserLoginData?.Ename ?? "Guest";



                    HttpContext.Session.SetString("Session", UserLoginData?.Pno ?? "N/A");
                    HttpContext.Session.SetString("UserName", UserLoginData?.Ename ?? "Guest");
                    HttpContext.Session.SetString("UserSession",login.UserId);

                    //store cookies

                    var cookieOptions = new CookieOptions
                    {
                        Expires = DateTimeOffset.Now.AddDays(1),
                        HttpOnly = false,
                        Secure = true,
                        IsEssential = true
                    };

                    Response.Cookies.Append("UserSession", login.UserId, cookieOptions);
                    Response.Cookies.Append("Session", UserLoginData?.Pno ?? "N/A", cookieOptions);
                    Response.Cookies.Append("UserName", UserLoginData?.Ename ?? "Guest", cookieOptions);






                   
                        return RedirectToAction("GeoFencing", "Geo");
                    
                }
                else
                {
                    ViewBag.FailedMsg = "Login Failed: Incorrect password";
                }
            }
            else
            {
                ViewBag.FailedMsg = "Login Failed: User not found";
            }

            return View(login);
        }

        public IActionResult ForgetPassword()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> ForgetPassword(AppLogin appLogin)
        {
            try
            {
                if (string.IsNullOrEmpty(appLogin.UserId))
                {
                    ViewBag.FailedMsg = "UserId is required";
                    return View(appLogin);
                }
                var user = await context.AppLogins
                    .FirstOrDefaultAsync(x => x.UserId == appLogin.UserId);

                if (user != null)
                {
                    string GenerateRandomString(int length)
                    {
                        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
                        Random random = new Random();
                        return new string(Enumerable.Repeat(chars, length)
                            .Select(s => s[random.Next(s.Length)]).ToArray());
                    }

                    var randomPassword = "XiADz" + GenerateRandomString(3) + "Ks" + GenerateRandomString(2) + "d";


                    var (hashedPassword, salt) = hash_Password.HashPassword(randomPassword);

                    user.Password = hashedPassword;
                    user.PasswordSalt = salt;

                    await context.SaveChangesAsync();

                    var emailId = user.Email;

                    var userData = await context1.AppEmployeeMasters
                        .Where(x => x.Pno == appLogin.UserId)
                        .Select(x => new
                        {
                            Name = x.Ename,
                            Dept = x.DepartmentName,
                        })
                        .FirstOrDefaultAsync();

                    if (userData != null)
                    {
                        string subject = $"{userData.Name} ({userData.Dept}): Your password has been changed";
                        string msg = $"<br/>Your password of Geofencing Attendance System has been changed to {randomPassword}<br/>" +
                                     "<br/>Kindly change the password after login.<br/>" +
                                     "Regards,<br/>" +
                                     "Tata Steel UISL<br/>";

                        await emailService.SendEmailAsync(emailId, "", "", subject, msg);

                        ViewBag.Msg = "Mail sent to: " + emailId;
                    }
                    else
                    {
                        Console.WriteLine("Email is null");
                    }
                }
                else
                {
                    ViewBag.FailedMsg = "UserId Not Found : Please Enter Correct UserId";
                    return View(appLogin);
                }
            }
            catch (SmtpException smtpEx)
            {
                smtpEx.Message.ToString();
            }
            catch (Exception ex)
            {
                ex.Message.ToString();
            }

            return View(appLogin);
        }

        public IActionResult ChangePassword()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> ChangePassword(AppLogin appLogin)
        {
            var pno = HttpContext.Session.GetString("Session");

            var user = await context.AppLogins
                .FirstOrDefaultAsync(x => x.UserId == pno);

            if (user != null)
            {

                bool isPasswordValid = hash_Password.VerifyPassword(appLogin.Password, user.Password, user.PasswordSalt);

                if (isPasswordValid)
                {
                    var (newHashedPassword, newSalt) = hash_Password.HashPassword(appLogin.NewPassword);

                    user.Password = newHashedPassword;
                    user.PasswordSalt = newSalt;

                    await context.SaveChangesAsync();

                    ViewBag.ChangePass = "Password Changed Successfully";
                }
                else
                {
                    ViewBag.FailedMsg = "Old Password Does Not Match!";
                }
            }
            else
            {
                ViewBag.FailedMsg = "Old Password Does Not Match!";
            }

            return View();
        }


		

    }
}
