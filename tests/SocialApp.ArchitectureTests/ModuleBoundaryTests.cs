using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace SocialApp.ArchitectureTests;

/// <summary>
/// Chặn tham chiếu chéo giữa các module (ADR-001): module chỉ được giao tiếp qua interface ở tầng
/// Application, không phụ thuộc trực tiếp vào namespace của module khác. GĐ0 các module còn rỗng nên
/// rule đúng "chân không" (WithoutRequiringPositiveResults) — nhưng khung test đã chạy và sẽ bắt vi
/// phạm ngay khi có code lệch.
/// </summary>
public sealed class ModuleBoundaryTests
{
    private static readonly string[] ModuleNames =
        ["Identity", "Profile", "SocialGraph", "Content", "Messaging", "Notification", "Moderation"];

    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(ModuleNames.Select(m => System.Reflection.Assembly.Load($"SocialApp.Modules.{m}")).ToArray())
        .Build();

    [Fact]
    public void Modules_should_not_depend_on_each_other()
    {
        foreach (var module in ModuleNames)
        {
            foreach (var other in ModuleNames.Where(o => o != module))
            {
                IArchRule rule = Types()
                    .That().ResideInNamespace($"SocialApp.Modules.{module}", useRegularExpressions: true)
                    .Should().NotDependOnAny(
                        Types().That().ResideInNamespace($"SocialApp.Modules.{other}", useRegularExpressions: true))
                    .Because($"module {module} không được tham chiếu trực tiếp module {other}")
                    .WithoutRequiringPositiveResults();

                rule.Check(Architecture);
            }
        }
    }
}
