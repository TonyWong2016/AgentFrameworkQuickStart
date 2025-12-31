using Spectre.Console;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace AgentFrameworkQuickStart.Tools
{
    public class CarModel
    {
        public string Name { get; set; } = "";
        public decimal Price { get; set; } // 万
        public string EnergyType { get; set; } = "";
        public List<string> Features { get; set; } = new();
    }
    public class CarTool
    {
        // 在 CarMasterAgent 类中定义工具
        [Description("根据预算、能源类型和需求点查询匹配的车型")]
        public string SearchCars(
            [Description("最高预算（单位：万）")] decimal maxBudget,
            [Description("能源类型（如：纯电、燃油、混动）")] string energyType,
            [Description("需求关键词列表")] string[] keywords)
        {
            AnsiConsole.MarkupLine("[yellow]命中汽车搜索工具（SearchCars）[/]");
            // 模拟数据库数据
            var db = new List<CarModel>
            {
                new() { Name = "极氪 001", Price = 26.9m, EnergyType = "纯电", Features = new(){ "智驾", "空悬", "大空间" } },
                new() { Name = "问界 M7", Price = 24.9m, EnergyType = "混动", Features = new(){ "智驾", "零重力座椅", "大空间" } },
                new() { Name = "宝马 3系", Price = 29.9m, EnergyType = "燃油", Features = new(){ "操控", "品牌", "运动" } },
                new() { Name = "小米 SU7", Price = 21.5m, EnergyType = "纯电", Features = new(){ "生态", "加速", "智驾" } },
                new() { Name = "坦克400 Hi4-Z", Price = 31.5m, EnergyType = "插混", Features = new(){ "硬派越野", "插混", "智驾" } }

            };

            // 执行过滤逻辑
            var results = db.Where(c => c.Price <= maxBudget && c.EnergyType.Contains(energyType)).ToList();

            if (!results.Any()) return "抱歉，根据您的偏好未找到匹配车型。";

            var sb = new StringBuilder("为您找到以下车型：\n");
            foreach (var car in results)
            {
                sb.AppendLine($"- {car.Name}: 价格 {car.Price}万, 特色: {string.Join("/", car.Features)}");
            }
            return sb.ToString();
        }
    }
}
