using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace DestinyTogether.Tests
{
    /// <summary>
    /// A barreira arquitetural, como teste e nao como combinado verbal.
    ///
    /// A regra que sustenta o projeto inteiro: a simulacao nao conhece a engine. E dela que vem
    /// (a) testes de partida completa em milissegundos, (b) a possibilidade de rodar um servidor
    /// sem tela, e (c) a garantia de que trocar placeholder por arte final nao pode quebrar
    /// regra de jogo — porque a regra nao tem como olhar para a arte.
    ///
    /// Se alguem adicionar "using UnityEngine" em DT.Sim para pegar um Mathf emprestado, este
    /// teste falha no mesmo dia, e nao seis meses depois quando a dependencia ja for irreversivel.
    /// </summary>
    public class ArchitectureTests
    {
        private static Assembly Load(string name)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
            Assert.IsNotNull(asm, $"Assembly {name} nao encontrado — o asmdef foi renomeado?");
            return asm;
        }

        private static void AssertNoEngineReference(string assemblyName)
        {
            var referenced = Load(assemblyName).GetReferencedAssemblies().Select(a => a.Name).ToArray();
            var engineRefs = referenced.Where(n =>
                n.StartsWith("UnityEngine", StringComparison.Ordinal) ||
                n.StartsWith("UnityEditor", StringComparison.Ordinal)).ToArray();

            Assert.IsEmpty(engineRefs,
                $"{assemblyName} referencia a engine ({string.Join(", ", engineRefs)}). " +
                "Isso quebra os testes rapidos e o servidor headless.");
        }

        [Test]
        public void DTCore_NaoConheceAEngine() => AssertNoEngineReference("DT.Core");

        [Test]
        public void DTSim_NaoConheceAEngine() => AssertNoEngineReference("DT.Sim");

        [Test]
        public void DTSim_NaoConheceConteudoNemApresentacao()
        {
            var referenced = Load("DT.Sim").GetReferencedAssemblies().Select(a => a.Name).ToArray();

            CollectionAssert.DoesNotContain(referenced, "DT.Data",
                "A simulacao recebe conteudo por IContentDatabase — nunca depende dos assets.");
            CollectionAssert.DoesNotContain(referenced, "DT.Presentation");
            CollectionAssert.DoesNotContain(referenced, "DT.UI");
            CollectionAssert.DoesNotContain(referenced, "DT.App");
        }

        [Test]
        public void Apresentacao_NaoDependeDaUI()
        {
            var referenced = Load("DT.Presentation").GetReferencedAssemblies().Select(a => a.Name).ToArray();
            CollectionAssert.DoesNotContain(referenced, "DT.UI");
            CollectionAssert.DoesNotContain(referenced, "DT.App");
        }

        [Test]
        public void Dados_NaoDependemDaApresentacao()
        {
            var referenced = Load("DT.Data").GetReferencedAssemblies().Select(a => a.Name).ToArray();
            CollectionAssert.DoesNotContain(referenced, "DT.Presentation");
            CollectionAssert.DoesNotContain(referenced, "DT.UI");
        }

        [Test]
        public void NenhumTipoDaSimulacao_ExpoeTipoDaEngine()
        {
            var simTypes = Load("DT.Sim").GetTypes().Where(t => t.IsPublic);

            foreach (var type in simTypes)
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    var ns = field.FieldType.Namespace ?? "";
                    Assert.IsFalse(ns.StartsWith("UnityEngine", StringComparison.Ordinal),
                        $"{type.Name}.{field.Name} expoe {field.FieldType.Name}, um tipo de engine.");
                }
            }
        }
    }
}
