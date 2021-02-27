using Newtonsoft.Json;

using System.Collections.Generic;
using System.Linq;

namespace FabricJSParser
{
    public static class FabricJSParser
    {
        public static IEnumerable<Table> Parse(string json)
        {
            var canvas = JsonConvert.DeserializeObject<FabricJSCanvas>(json);

            var list = new List<Table>();

            foreach (var group in canvas.objects.Where(o => o.type == "group"))
            {
                foreach (var item in group.objects)
                {
                    item.top += group.height / 2;
                    item.left += group.width / 2;

                    item.width *= item.scaleX;
                    item.height *= item.scaleY;
                }

                list.Add(new Table
                {
                    Rows = group.objects.Where(o => o.IsHorizontalRect())
                    .Select(r => r.ToRegion(group.left, group.top))
                    .ToList(),

                    Columns = group.objects.Where(o => o.IsVerticalRect())
                    .Select(r => r.ToRegion(group.left, group.top))
                    .ToList(),
                });
            }

            return list;
        }
    }
}
