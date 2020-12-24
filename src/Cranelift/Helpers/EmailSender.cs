using FluentEmail.Core;

using System.Threading.Tasks;

namespace Cranelift.Helpers
{
    public class EmailOptions
    {
        public string FromAddress { get; set; }
        public string Host { get; set; }
        public string Password { get; set; }
        public int Port { get; set; }
    }

    public class EmailSender
    {
        private readonly IFluentEmailFactory _fluentEmailFactory;

        public EmailSender(IFluentEmailFactory fluentEmailFactory)
        {
            _fluentEmailFactory = fluentEmailFactory;
        }

        public async Task SendEmail(string toEmail, string subject, string htmlContent)
        {
            await _fluentEmailFactory.Create()
                        .To(toEmail)
                        .Subject(subject)
                        .Body(htmlContent, true)
                        .SendAsync();
        }
    }
}
