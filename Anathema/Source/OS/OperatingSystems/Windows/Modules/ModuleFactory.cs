﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Anathema.MemoryManagement.Internals;
using Anathema.MemoryManagement.Memory;

namespace Anathema.MemoryManagement.Modules
{
    /// <summary>
    /// Class providing tools for manipulating modules and libraries.
    /// </summary>
    public class ModuleFactory : IFactory
    {
        /// <summary>
        /// The reference of the <see cref="MemoryManagement.WindowsOSInterface"/> object.
        /// </summary>
        protected readonly WindowsOSInterface MemorySharp;
        /// <summary>
        /// The list containing all injected modules (writable).
        /// </summary>
        protected readonly List<InjectedModule> InternalInjectedModules;

        /// <summary>
        /// A collection containing all injected modules.
        /// </summary>
        public IEnumerable<InjectedModule> InjectedModules { get { return InternalInjectedModules.AsReadOnly(); } }

        /// <summary>
        /// Gets the main module for the remote process.
        /// </summary>
        public RemoteModule MainModule { get { return FetchModule(MemorySharp.Native.MainModule); } }

        /// <summary>
        /// Gets the modules that have been loaded in the remote process.
        /// </summary>
        public IEnumerable<RemoteModule> RemoteModules { get { return NativeModules.Select(FetchModule); } }

        /// <summary>
        /// Gets the native modules that have been loaded in the remote process.
        /// </summary>
        internal IEnumerable<ProcessModule> NativeModules { get { return MemorySharp.Native.Modules.Cast<ProcessModule>(); } }

        #region This
        /// <summary>
        /// Gets a pointer from the remote process.
        /// </summary>
        /// <param name="address">The address of the pointer.</param>
        /// <returns>A new instance of a <see cref="RemotePointer"/> class.</returns>
        public RemotePointer this[IntPtr address]
        {
            get { return new RemotePointer(MemorySharp, address); }
        }

        /// <summary>
        /// Gets the specified module in the remote process.
        /// </summary>
        /// <param name="moduleName">The name of module (not case sensitive).</param>
        /// <returns>A new instance of a <see cref="RemoteModule"/> class.</returns>
        public RemoteModule this[string moduleName]
        {
            get { return FetchModule(moduleName); }
        }

        #endregion

        #region Constructor/Destructor
        /// <summary>
        /// Initializes a new instance of the <see cref="ModuleFactory"/> class.
        /// </summary>
        /// <param name="MemorySharp">The reference of the <see cref="MemoryManagement.WindowsOSInterface"/> object.</param>
        internal ModuleFactory(WindowsOSInterface MemorySharp)
        {
            // Save the parameter
            this.MemorySharp = MemorySharp;
            // Create a list containing all injected modules
            InternalInjectedModules = new List<InjectedModule>();
        }

        /// <summary>
        /// Frees resources and perform other cleanup operations before it is reclaimed by garbage collection.
        /// </summary>
        ~ModuleFactory()
        {
            Dispose();
        }

        #endregion

        #region Methods
        #region Dispose (implementation of IFactory)
        /// <summary>
        /// Releases all resources used by the <see cref="ModuleFactory"/> object.
        /// </summary>
        public virtual void Dispose()
        {
            // Release all injected modules which must be disposed
            foreach (InjectedModule InjectedModule in InternalInjectedModules.Where(x => x.MustBeDisposed))
            {
                InjectedModule.Dispose();
            }

            // Clean the cached functions related to this process
            foreach (var CachedFunction in RemoteModule.CachedFunctions.ToArray())
            {
                if (CachedFunction.Key.Item2 == MemorySharp.Handle)
                    RemoteModule.CachedFunctions.Remove(CachedFunction);
            }

            // Avoid the finalizer
            GC.SuppressFinalize(this);
        }

        #endregion
        #region Eject
        /// <summary>
        /// Frees the loaded dynamic-link library (DLL) module and, if necessary, decrements its reference count.
        /// </summary>
        /// <param name="Module">The module to eject.</param>
        public void Eject(RemoteModule Module)
        {
            // If the module is valid
            if (!Module.IsValid)
                return;

            // Find if the module is an injected one
            InjectedModule InjectedModule = InternalInjectedModules.FirstOrDefault(m => m.Equals(Module));

            if (InjectedModule != null)
                InternalInjectedModules.Remove(InjectedModule);

            // Eject the module
            RemoteModule.InternalEject(MemorySharp, Module);
        }

        /// <summary>
        /// Frees the loaded dynamic-link library (DLL) module and, if necessary, decrements its reference count.
        /// </summary>
        /// <param name="ModuleName">The name of module to eject.</param>
        public void Eject(String ModuleName)
        {
            // Fint the module to eject
            RemoteModule module = RemoteModules.FirstOrDefault(m => m.Name == ModuleName);

            // Eject the module is it's valid
            if (module != null)
                RemoteModule.InternalEject(MemorySharp, module);
        }

        #endregion
        #region FetchModule (protected)
        /// <summary>
        /// Fetches a module from the remote process.
        /// </summary>
        /// <param name="ModuleName">A module name (not case sensitive). If the file name extension is omitted, the default library extension .dll is appended.</param>
        /// <returns>A new instance of a <see cref="RemoteModule"/> class.</returns>
        protected RemoteModule FetchModule(String ModuleName)
        {
            // Convert module name with lower chars
            ModuleName = ModuleName.ToLower();

            // Check if the module name has an extension
            if (!Path.HasExtension(ModuleName))
                ModuleName += ".dll";


            // Fetch and return the module
            return new RemoteModule(MemorySharp, NativeModules.First(m => m.ModuleName.ToLower() == ModuleName));
        }

        /// <summary>
        /// Fetches a module from the remote process.
        /// </summary>
        /// <param name="Module">A module in the remote process.</param>
        /// <returns>A new instance of a <see cref="RemoteModule"/> class.</returns>
        private RemoteModule FetchModule(ProcessModule Module)
        {
            return FetchModule(Module.ModuleName);
        }

        #endregion
        #region Inject
        /// <summary>
        /// Injects the specified module into the address space of the remote process.
        /// </summary>
        /// <param name="Path">The path of the module. This can be either a library module (a .dll file) or an executable module (an .exe file).</param>
        /// <param name="MustBeDisposed">The module will be ejected when the finalizer collects the object.</param>
        /// <returns>A new instance of the <see cref="InjectedModule"/>class.</returns>
        public InjectedModule Inject(String Path, Boolean MustBeDisposed = true)
        {
            // Injects the module
            var Module = InjectedModule.InternalInject(MemorySharp, Path);

            // Add the module in the list
            InternalInjectedModules.Add(Module);

            // Return the module
            return Module;
        }

        #endregion
        #endregion

    } // End class

} // End namespace