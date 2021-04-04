using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ddresize
{
    public class Arguments
    {
            private List<IArgument> _args;

            private Arguments()
            {
                _args = new List<IArgument>();
                UsagePrinter = new UsagePrinter();
            }

            public Arguments(string description)
                : this(description, Assembly.GetEntryAssembly())
            {
            }

            public Arguments(string description, string execname)
                : this()
            {
                UsagePrinter = new UsagePrinter();
                UsagePrinter.Description = description;
                UsagePrinter.Executable = execname;
            }

            public Arguments(string description, Assembly programAssembly) : this()
            {
                UsagePrinter = new UsagePrinter();
                UsagePrinter.Description = description;
                UsagePrinter.Executable = programAssembly.GetName().Name;
            }

            public string ExecutableName
            {
                get { return UsagePrinter.Executable; }
            }

            public UsagePrinter UsagePrinter { get; set; }

            public bool IsValid { get; set; }

            public List<string> Errors { get; private set; }

            public void PrintUsage(TextWriter writer)
            {
                UsagePrinter.PrintUsage(writer, _args);
            }

            public void PrintErrors(TextWriter writer)
            {
                if (Errors != null)
                {
                    foreach (string e in Errors)
                        writer.WriteLine(e);
                }
            }

            public Argument<PType> Add<PType>(string shortName, string longName, string description, bool isRequired)
            {
                foreach (IArgument info in _args)
                {
                    if (info.ShortName == shortName)
                        throw new ArgumentException("An argument with the same name already exists: " + shortName);
                    if (info.LongName == longName)
                        throw new ArgumentException("An argument with the same name already exists: " + longName);
                }

                var arg = new Argument<PType>(
                    this,
                    shortName,
                    longName,
                    description,
                    isRequired);
                _args.Add(arg);
                return arg;
            }

            public SwitchArgument AddSwitch(string shortName, string longName, string description)
            {
                foreach (IArgument info in _args)
                {
                    if (info.ShortName == shortName)
                        throw new ArgumentException("An argument with the same name already exists: " + shortName);
                    if (info.LongName == longName)
                        throw new ArgumentException("An argument with the same name already exists: " + longName);
                }

                var arg = new SwitchArgument(this, shortName, longName, description);
                _args.Add(arg);
                return arg;
            }

            public void Parse(string[] rawArguments, ArgumentParseOptions parseOptions = ArgumentParseOptions.None)
            {
                foreach (var arg in _args)
                    arg.ClearValues();
                IsValid = false;
                Errors = new List<string>();
                IArgument[] args = _args.ToArray();
                var tokens = new ArgumentTokenStream(rawArguments, parseOptions);
                while (tokens.Advance())
                {
                    string argumentNameToken = tokens.CurrentToken;
                    string argumentName = argumentNameToken.TrimStart('-', '/');
                    IArgument matchingArgument = args.FirstOrDefault(arg =>
                        arg.ShortName == argumentName || arg.LongName == argumentName);

                    if (matchingArgument == null)
                        Errors.Add(string.Format("unknown argument '{0}'", argumentName));
                    else if (matchingArgument is SwitchArgument)
                    {
                        matchingArgument.AddValue(true);
                        matchingArgument.IsMissing = false;
                    }
                    else if (tokens.Advance())
                    {
                        object actualValue;
                        if (TryGetActualValue(tokens.CurrentToken, matchingArgument, out actualValue))
                        {
                            matchingArgument.AddValue(actualValue);
                            matchingArgument.IsMissing = false;
                        }
                        else
                            Errors.Add(string.Format("invalid argument value for {0}: {1}", argumentNameToken,
                                tokens.CurrentToken));
                    }
                    else
                        Errors.Add("The argument '" + argumentNameToken + "' was specified but no value was provided");
                }

                for (int i = 0; i < args.Length; i++)
                {
                    IArgument arg = args[i];
                    if (arg.IsRequired && arg.IsMissing)
                        Errors.Add(string.Format("missing argument '{0}'", arg.LongName));
                }

                IsValid = Errors.Count == 0;
            }

            private bool TryGetActualValue(string tokenValue, IArgument matchingArgument, out object actualValue)
            {
                actualValue = null;
                try
                {
                    if (typeof(int) == matchingArgument.Type)
                        actualValue = int.Parse(tokenValue);
                    else if (typeof(short) == matchingArgument.Type)
                        actualValue = short.Parse(tokenValue);
                    else if (typeof(long) == matchingArgument.Type)
                        actualValue = long.Parse(tokenValue);
                    else if (typeof(DateTime) == matchingArgument.Type)
                        actualValue = DateTime.Parse(tokenValue);
                    else if (typeof(string) == matchingArgument.Type)
                        actualValue = tokenValue;
                    else
                        actualValue = Convert.ChangeType(tokenValue, matchingArgument.Type);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public class SwitchArgument : Argument<bool>
        {
            public SwitchArgument(Arguments arguments, string shortName, string longName, string description)
                : base(arguments, shortName, longName, description, false)
            {
                DefaultValue = false;
            }
        }

        public class Argument<T> : IArgument
        {
            private Arguments _parser;
            private List<T> _values;

            public Argument(
                Arguments parser,
                string shortName,
                string longName,
                string description,
                bool isRequired)
            {
                _parser = parser;
                ShortName = shortName;
                LongName = longName;
                Description = description;
                IsRequired = isRequired;
                IsMissing = true;
                _values = new List<T>();
            }

            public T Value
            {
                get
                {
                    if (!_parser.IsValid)
                        throw new InvalidOperationException(
                            "Unable to get the value of " + ShortName + ".  The command line arguments are not valid.");

                    if (IsMissing)
                        return DefaultValue;

                    return _values.FirstOrDefault();
                }
            }

            public List<T> Values
            {
                get { return _values; }
            }

            public T DefaultValue { get; set; }

            public string ShortName { get; private set; }

            public string LongName { get; private set; }

            public string Description { get; private set; }

            public bool IsRequired { get; private set; }

            public bool IsMissing { get; set; }

            public bool WasProvided
            {
                get { return !IsMissing; }
            }

            public Type Type
            {
                get { return typeof(T); }
            }

            object IArgument.DefaultValue
            {
                get { return DefaultValue; }
            }

            void IArgument.AddValue(object value)
            {
                _values.Add((T) value);
            }

            void IArgument.ClearValues()
            {
                _values.Clear();
            }
        }

        public interface IArgument
        {
            object DefaultValue { get; }
            bool IsRequired { get; }
            string ShortName { get; }
            Type Type { get; }
            string LongName { get; }
            bool IsMissing { get; set; }
            string Description { get; }
            void AddValue(object val);
            void ClearValues();
        }

        public class UsagePrinter
        {
            private IEnumerable<IArgument> _args;
            private TextWriter _writer;

            public string Executable { get; set; }

            public string Description { get; set; }

            public virtual void PrintUsage(TextWriter writer, IEnumerable<IArgument> args)
            {
                this._writer = writer;
                this._args = args;
                if (!string.IsNullOrEmpty(Executable) && !string.IsNullOrEmpty(Description))
                    PrintProgramLine();
                if (!string.IsNullOrEmpty(Executable))
                    PrintUsageLine();
                PrintArgs();
            }

            private void PrintProgramLine()
            {
                //_writer.Write(Executable);
                //_writer.Write(" - ");
                _writer.Write(Description);
                _writer.WriteLine();
                _writer.WriteLine();
            }

            private void PrintUsageLine()
            {
                _writer.Write("usage: ");
                _writer.Write(Executable);
                _writer.Write(' ');
                foreach (IArgument arg in _args)
                {
                    if (!arg.IsRequired)
                        _writer.Write('[');
                    _writer.Write('-');
                    _writer.Write(arg.ShortName);
                    if (!(arg is SwitchArgument))
                    {
                        _writer.Write(" <");
                        //_writer.Write(GetTypeString(arg.Type));
                        _writer.Write(arg.LongName);
                        _writer.Write('>');
                    }

                    if (!arg.IsRequired)
                        _writer.Write(']');
                    _writer.Write(' ');
                }

                _writer.WriteLine();
                _writer.WriteLine();
            }

            private void PrintArgs()
            {
                foreach (IArgument arg in _args)
                {
                    _writer.Write(' ');
                    _writer.Write(' ');
                    _writer.Write(arg.ShortName);
                    //_writer.Write(',');
                    //_writer.Write(arg.LongName);
                    _writer.Write(" : ");
                    //_writer.Write(GetTypeString(arg.Type));
                    //_writer.Write("; ");
                    if (arg.IsRequired)
                        _writer.Write("required  ");
                    else
                    {
                        _writer.Write("optional  ");
                        if (!(arg.Type.IsValueType && Equals(arg.DefaultValue, Activator.CreateInstance(arg.Type))) &&
                            arg.DefaultValue != null)
                        {
                            _writer.Write("default is ");
                            _writer.Write(arg.DefaultValue);
                            _writer.Write(". ");
                        }
                    }

                    _writer.Write("- ");
                    _writer.Write(arg.Description);
                    _writer.WriteLine();
                }
            }

            private string GetTypeString(Type type)
            {
                if (typeof(string) == type)
                    return "string";
                else if (typeof(int) == type)
                    return "int";
                else if (typeof(double) == type)
                    return "double";
                else if (typeof(byte) == type)
                    return "byte";
                else if (typeof(short) == type)
                    return "short";
                else if (typeof(uint) == type)
                    return "uint";
                else if (typeof(long) == type)
                    return "long";
                else if (typeof(ulong) == type)
                    return "ulong";
                else if (typeof(decimal) == type)
                    return "decimal";
                else if (typeof(DateTime) == type)
                    return "date";
                else if (typeof(char) == type)
                    return "char";
                else if (typeof(float) == type)
                    return "float";
                else if (typeof(sbyte) == type)
                    return "sbyte";
                else if (typeof(ushort) == type)
                    return "ushort";
                else
                    return type.Name;
            }
        }

        public class ArgumentTokenStream
        {
            private readonly List<string> _args;
            private int _cursor;
            private bool _trimQuotes;

            public ArgumentTokenStream(IEnumerable<string> args, ArgumentParseOptions parseOptions)
            {
                bool colonIsUsedForArgumentSeparator = (parseOptions & ArgumentParseOptions.ColonSeparatesArgValues) ==
                                                       ArgumentParseOptions.ColonSeparatesArgValues;
                _trimQuotes = (parseOptions & ArgumentParseOptions.TrimQuotes) == ArgumentParseOptions.TrimQuotes;
                IEnumerable<string> tokens = args.Select(a => a ?? "");
                if (!colonIsUsedForArgumentSeparator)
                    _args = tokens.ToList();
                else
                {
                    _args = new List<string>();
                    foreach (string arg in tokens)
                    {
                        int colonIndex = arg.IndexOf(':');
                        if (colonIndex > 0)
                        {
                            _args.Add(arg.Substring(0, colonIndex));
                            if (colonIndex + 1 < arg.Length)
                                _args.Add(arg.Substring(colonIndex + 1));
                        }
                        else
                            _args.Add(arg);
                    }
                }

                _cursor = -1;
            }

            public string CurrentToken { get; private set; }

            public bool Advance()
            {
                _cursor++;
                bool hasMore = _cursor < _args.Count;
                if (hasMore && _trimQuotes)
                    CurrentToken = _args[_cursor].TrimStart('"', '\'').TrimEnd('"', '\'');
                else if (hasMore)
                    CurrentToken = _args[_cursor];
                else
                    CurrentToken = null;
                return hasMore;
            }
        }

        [Flags]
        public enum ArgumentParseOptions
        {
            None = 0,
            TrimQuotes = 1,
            ColonSeparatesArgValues = 2
        }
    
}