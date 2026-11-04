using Soundboard.Core.Text;

namespace Soundboard.Core.Tests;

[TestClass]
public static class TestSetup
{
    /// <summary>
    /// The older tests check the German texts, so the suite runs in German. Tests about English
    /// or switching set the language themselves and put German back afterwards. The tests do not
    /// run in parallel (no [Parallelize]), so the shared language is safe.
    /// </summary>
    [AssemblyInitialize]
    public static void Init(TestContext _) => L.SetLanguage(L.German);
}
