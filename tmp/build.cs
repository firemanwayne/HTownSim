using System;
using System.Linq;
using System.Reflection;
public static class BuildTmp {
  public static string Run() {
    var t = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } }).First(x => x.FullName == "HTownSim.EditorTools.BuildGame");
    return "" + t.GetMethod("Build", BindingFlags.Static|BindingFlags.Public).Invoke(null, null);
  }
}
