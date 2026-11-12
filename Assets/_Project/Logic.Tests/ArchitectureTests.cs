// Правила архитектуры из TDD 4.1, которые проверяются автоматически (Window > General > Test Runner > EditMode).
using System;
using System.Linq;
using NUnit.Framework;

namespace Game.Logic.Tests
{
    public class ArchitectureTests
    {
        static System.Reflection.Assembly Logic =>
            AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Game.Logic");

        [Test]
        public void Logic_DoesNotReferenceUnity()
        {
            var unity = Logic.GetReferencedAssemblies().Select(a => a.Name)
                .Where(n => n.StartsWith("Unity", StringComparison.Ordinal)).ToArray();
            Assert.IsEmpty(unity, "Game.Logic ссылается на " + string.Join(", ", unity));
        }

        [Test]
        public void Logic_DoesNotReferenceView()
        {
            var view = Logic.GetReferencedAssemblies().Select(a => a.Name)
                .Where(n => n.StartsWith("Game.View", StringComparison.Ordinal)).ToArray();
            Assert.IsEmpty(view, "логика не должна знать про визуал");
        }
    }
}
