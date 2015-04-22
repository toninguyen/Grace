using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Grace.Execution;

namespace Grace.Runtime
{
    /// <summary>Local scope of a method</summary>
    public class LocalScope : GraceObject
    {
        /// <summary>Object to redirect any requests resolved on the
        /// surrounding scope to</summary>
        public GraceObject RedirectSurrounding { get; set; }

        /// <summary>Reusable method for reading a local variable</summary>
        public static readonly LocalReaderMethod Reader = new LocalReaderMethod();

        /// <summary>Reusable method for writing a local variable</summary>
        public static readonly LocalWriterMethod Writer = new LocalWriterMethod();

        /// <summary>Mapping of variable names to values</summary>
        public Dictionary<string, GraceObject> locals = new Dictionary<string, GraceObject>();

        private string name = "<anon>";

        /// <summary>Empty anonymous scope</summary>
        public LocalScope() { }

        /// <summary>Empty named scope</summary>
        /// <param name="name">Name of this scope for debugging</param>
        public LocalScope(string name)
        {
            this.name = name;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            if (name != null)
                return "GraceObject[" + name + "]";
            return "GraceObject";
        }

        /// <summary>Add a new def to this scope</summary>
        /// <param name="name">Name of def to create</param>
        public void AddLocalDef(string name)
        {
            AddLocalDef(name, GraceObject.Uninitialised);
        }

        /// <summary>Add a new def to this scope</summary>
        /// <param name="name">Name of def to create</param>
        /// <param name="val">Value to set def to</param>
        /// <returns>Method that was added</returns>
        public override MethodNode AddLocalDef(string name, GraceObject val)
        {
            locals[name] = val;
            AddMethod(name, Reader);
            return Reader;
        }

        /// <summary>Add a new var to this scope</summary>
        /// <param name="name">Name of var to create</param>
        public void AddLocalVar(string name)
        {
            AddLocalVar(name, GraceObject.Uninitialised);
        }

        /// <summary>Add a new var to this scope</summary>
        /// <param name="name">Name of var to create</param>
        /// <param name="val">Value to set var to</param>
        /// <returns>Pair of methods that were added</returns>
        public override ReaderWriterPair AddLocalVar(string name,
                GraceObject val)
        {
            locals[name] = val;
            AddMethod(name, Reader);
            AddMethod(name + ":=", Writer);
            return new ReaderWriterPair { Read = Reader, Write = Writer };
        }

        /// <summary>Access variables in this scope</summary>
        /// <value>This property accesses the Dictionary field locals</value>
        public GraceObject this[string s]
        {
            get
            {
                return locals[s];
            }
            set
            {
                locals[s] = value;
            }
        }
    }

    /// <summary>Method to read a local variable</summary>
    public class LocalReaderMethod : MethodNode
    {

        ///
        public LocalReaderMethod()
            : base(null, null)
        {
        }

        /// <inheritdoc/>
        /// <remarks>This method uses the indexer on the LocalScope
        /// object the method was requested on.</remarks>
        public override GraceObject Respond(EvaluationContext ctx, GraceObject self, MethodRequest req)
        {
            checkAccessibility(ctx, req);
            LocalScope s = self as LocalScope;
            string name = req.Name;
            Interpreter.Debug("local '" + name + "' is " + s[name]);
            return s[name];
        }
    }

    /// <summary>Method to write a local variable</summary>
    public class LocalWriterMethod : MethodNode
    {
        ///
        public LocalWriterMethod()
            : base(null, null)
        {

        }

        /// <inheritdoc/>
        /// <remarks>This method uses the indexer on the LocalScope
        /// object the method was requested on.</remarks>
        public override GraceObject Respond(EvaluationContext ctx, GraceObject self, MethodRequest req)
        {
            checkAccessibility(ctx, req);
            LocalScope s = self as LocalScope;
            string name = req.Name.Substring(0, req.Name.Length - 2);
            s[name] = req[0].Arguments[0];
            Interpreter.Debug("local '" + name + "' set to " + s[name]);
            return GraceObject.Uninitialised;
        }
    }

}
