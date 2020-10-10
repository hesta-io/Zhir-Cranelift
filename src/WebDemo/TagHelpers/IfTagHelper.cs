using Microsoft.AspNetCore.Razor.TagHelpers;

using System.Threading.Tasks;

namespace WebDemo.TagHelpers
{
    [HtmlTargetElement("div")]
    public class IfTagHelper : TagHelper
    {
        [HtmlAttributeName("if")]
        public bool? If { get; set; }

        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            if (If != false)
            {
                await base.ProcessAsync(context, output);
            }
            else
            {
                output.SuppressOutput();
                output.Content = await output.GetChildContentAsync();
            }
        }
    }
}
