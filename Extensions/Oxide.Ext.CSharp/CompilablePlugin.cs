﻿using System;
using System.Reflection;

using Oxide.Core;

namespace Oxide.Plugins
{
    public class CompilablePlugin : CompilableFile
    {
        private static object compileLock = new object();
        
        public CompiledAssembly LastGoodAssembly;
        public bool IsLoading;
        
        public CompilablePlugin(CSharpExtension extension, CSharpPluginLoader loader, string directory, string name)
            : base(extension, loader, directory, name)
        {

        }

        protected override void OnLoadingStarted()
        {
            Loader.PluginLoadingStarted(this);
        }

        protected override void OnCompilationRequested()
        {
            Loader.CompilationRequested(this);
        }

        internal void LoadPlugin(Action<CSharpPlugin> callback = null)
        {
            if (CompiledAssembly == null)
            {
                Interface.Oxide.LogError("Load called before a compiled assembly exists: " + Name);
                RemoteLogger.Error("Load called before a compiled assembly exists: " + Name);
                return;
            }

            loadCallback = callback;

            CompiledAssembly.LoadAssembly(loaded =>
            {
                if (!loaded)
                {
                    if (callback != null) callback(null);
                    return;
                }

                if (CompilerErrors != null)
                {
                    InitFailed("Unable to load " + ScriptName + ". " + CompilerErrors);
                    return;
                }

                var type = CompiledAssembly.LoadedAssembly.GetType("Oxide.Plugins." + Name);
                if (type == null)
                {
                    InitFailed("Unable to find main plugin class: " + Name);
                    return;
                }

                CSharpPlugin plugin = null;
                try
                {
                    plugin = Activator.CreateInstance(type) as CSharpPlugin;
                }
                catch (MissingMethodException)
                {
                    InitFailed("Main plugin class should not have a constructor defined: " + Name);
                    return;
                }
                catch (TargetInvocationException invocation_exception)
                {
                    var ex = invocation_exception.InnerException;
                    InitFailed("Unable to load " + ScriptName + ". " + ex.ToString());
                    return;
                }
                catch (Exception ex)
                {
                    InitFailed("Unable to load " + ScriptName + ". " + ex.ToString());
                    return;
                }

                if (plugin == null)
                {
                    RemoteLogger.Error("Plugin assembly failed to load: " + ScriptName);
                    InitFailed("Plugin assembly failed to load: " + ScriptName);
                    return;
                }

                plugin.SetPluginInfo(ScriptName, ScriptPath);
                plugin.Watcher = Extension.Watcher;
                plugin.Loader = Loader;

                if (!Interface.Oxide.PluginLoaded(plugin))
                {
                    InitFailed();
                    return;
                }

                if (!CompiledAssembly.IsBatch) LastGoodAssembly = CompiledAssembly;
                if (callback != null) callback(plugin);
            });
        }

        internal override void OnCompilationStarted()
        {
            base.OnCompilationStarted();

            // Enqueue compilation of any plugins which depend on this plugin
            foreach (var plugin in Interface.Oxide.RootPluginManager.GetPlugins())
            {
                if (!(plugin is CSharpPlugin)) continue;
                var compilable_plugin = CSharpPluginLoader.GetCompilablePlugin(Directory, plugin.Name);
                if (!compilable_plugin.Requires.Contains(Name)) continue;
                compilable_plugin.CompiledAssembly = null;
                Loader.Load(compilable_plugin);
            }
        }
                
        protected override void InitFailed(string message = null)
        {
            base.InitFailed(message);
            if (LastGoodAssembly == null)
            {
                Interface.Oxide.LogInfo("No previous version to rollback plugin: {0}", ScriptName);
                return;
            }
            if (CompiledAssembly == LastGoodAssembly)
            {
                Interface.Oxide.LogInfo("Previous version of plugin failed to load: {0}", ScriptName);
                return;
            }
            Interface.Oxide.LogInfo("Rolling back plugin to last good version: {0}", ScriptName);
            CompiledAssembly = LastGoodAssembly;
            CompilerErrors = null;
            LoadPlugin();
        }
    }
}
