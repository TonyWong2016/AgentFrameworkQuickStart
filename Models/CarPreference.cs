using System;
using System.Collections.Generic;
using System.Text;

namespace AgentFrameworkQuickStart.Models
{
    public class CarPreference
    {
        public decimal BudgetMax { get; set; } // 预算上限
        public string EnergyType { get; set; } = "未指定"; // 燃油、纯电、混动
        public List<string> MustHaves { get; set; } = new(); // 必选配置，如：7座、智驾、空悬
        public string LastRecommendedModel { get; set; } = ""; // 上次推荐的车型
    }
}
