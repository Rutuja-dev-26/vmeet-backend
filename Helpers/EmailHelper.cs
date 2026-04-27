using System.Configuration;
using System.Net;
using System.Net.Mail;

namespace VMeetTool.Helpers
{
    public class EmailHelper
    {
        public static void SendPasswordResetEmail(string toEmail, string toName, string resetLink)
        {
            string smtpHost     = ConfigurationManager.AppSettings["SmtpHost"];
            int    smtpPort     = int.Parse(ConfigurationManager.AppSettings["SmtpPort"] ?? "587");
            string smtpUser     = ConfigurationManager.AppSettings["SmtpUser"];
            string smtpPass     = ConfigurationManager.AppSettings["SmtpPass"];
            string fromEmail    = ConfigurationManager.AppSettings["SmtpFromEmail"];
            string fromName     = ConfigurationManager.AppSettings["SmtpFromName"] ?? "VMeet";

            string body = $@"
<html>
<body style=""font-family:Arial,sans-serif;max-width:600px;margin:0 auto;color:#333;"">
  <h2 style=""color:#007bff;"">Reset Your VMeet Password</h2>
  <p>Hi {toName},</p>
  <p>We received a request to reset the password for your VMeet account.</p>
  <p>Click the button below to set a new password. This link will expire in <strong>15 minutes</strong>.</p>
  <p style=""margin:30px 0;"">
    <a href=""{resetLink}""
       style=""background-color:#007bff;color:#fff;padding:12px 28px;text-decoration:none;border-radius:5px;font-size:15px;"">
      Reset Password
    </a>
  </p>
  <p>Or copy and paste this link into your browser:</p>
  <p style=""word-break:break-all;color:#555;font-size:13px;"">{resetLink}</p>
  <hr style=""border:none;border-top:1px solid #eee;margin:30px 0;""/>
  <p style=""color:#999;font-size:12px;"">
    If you did not request a password reset, please ignore this email. Your password will not change.
  </p>
  <p style=""color:#999;font-size:12px;"">— VMeet Team</p>
</body>
</html>";

            using (var client = new SmtpClient(smtpHost, smtpPort))
            {
                client.Credentials = new NetworkCredential(smtpUser, smtpPass);
                client.EnableSsl   = true;

                var mail = new MailMessage
                {
                    From       = new MailAddress(fromEmail, fromName),
                    Subject    = "Reset Your VMeet Password",
                    Body       = body,
                    IsBodyHtml = true
                };
                mail.To.Add(new MailAddress(toEmail, toName));

                client.Send(mail);
            }
        }
    }
}
