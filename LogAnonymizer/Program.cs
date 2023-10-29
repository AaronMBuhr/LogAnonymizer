using CommandLine;
using CommandLine.Text;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public class Options
{
    [Option( 'c', "config", Required = false, HelpText = "Config filename." )]
    public string ConfigFilename { get; set; } = "LogAnonymizer.yaml";

    [Option( 'i', "input", Required = true, HelpText = "Input filename." )]
    public string InputFilename { get; set; }

    [Option( 'o', "output", Required = false, HelpText = "Output filename." )]
    public string OutputFilename { get; set; }

    [Option( 'h', "help", Required = false, HelpText = "Show help message." )]
    public bool ShowHelp { get; set; }
}

public class Program
{
    static ConcurrentDictionary<string, string> _static_replacements = new ConcurrentDictionary<string, string>();
    static ConcurrentDictionary<string, string> _exp_replacements = new ConcurrentDictionary<string, string>();
    static ConcurrentDictionary<string, string> _iterated_replacements = new ConcurrentDictionary<string, string>();
    static ConcurrentDictionary<string, string> _replaced_values = new ConcurrentDictionary<string, string>();
    static string _chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    static Random _random = new Random();

    private static IEnumerable<string> GenerateAlphaSequence( int length )
    {
        if( length == 1 )
        {
            foreach( char c in _chars )
            {
                yield return c.ToString();
            }
        }
        else
        {
            foreach( string prefix in GenerateAlphaSequence( length - 1 ) )
            {
                foreach( char c in _chars )
                {
                    yield return prefix + c;
                }
            }
        }
    }

    public static string StaticMatchEvaluatorFunction( Match match )
    {
        return _static_replacements.TryGetValue( match.Value, out var replacement ) ? replacement : match.Value;
    }

    public static string ExpressionMatchEvaluatorFunction( Match m )
    {
        foreach( var groupName in m.Groups.Keys )
        {
            // Check if the group has a match and if the group name exists as a key in the ConcurrentDictionary
            if( m.Groups[groupName].Success && _iterated_replacements.ContainsKey( groupName ) )
            {
                var orig_key = m.Groups[0].Value;
                if( !_replaced_values.ContainsKey( orig_key ) )
                {
                    string replacement = _iterated_replacements[groupName];
                    replacement = ParseReplacement( replacement );
                    _replaced_values[orig_key] = m.Result( replacement );
                }
                return _replaced_values[orig_key];
            }
        }
        return m.Value; // Return the original matched value if no replacement is found
    }

    public static string GenerateRandomString( int length )
    {
        return new string( Enumerable.Repeat( _chars, length )
            .Select( s => s[_random.Next( s.Length )] ).ToArray() );
    }

    public static string GenerateRandomEmail( string hostname )
    {
        // Generates random local part with length between 5 and 10 characters.
        var name = GenerateRandomString( _random.Next( 5, 11 ) );
        return $"{name}@{hostname}";
    }

    public static string GetGreekLetter( int n )
    {
        string[] greekLetters =
        {
        "alpha", "beta", "gamma", "delta", "epsilon",
        "zeta", "eta", "theta", "iota", "kappa",
        "lambda", "mu", "nu", "xi", "omicron",
        "pi", "rho", "sigma", "tau", "upsilon",
        "phi", "chi", "psi", "omega"
    };

        if( n >= 0 && n < greekLetters.Length )
        {
            return greekLetters[n];
        }
        else
        {
            throw new ArgumentOutOfRangeException( $"Value {n} is out of the range for Greek letters." );
        }
    }


    static string GenerateRandomHostname( string tld )
    {
        return string.Format( "{0}.{1}.{2}", GetGreekLetter( _random.Next( 24 ) ), GetGreekLetter( _random.Next( 24 ) ), tld );
    }
    static string GenerateRandomNumber( int length )
    {
        StringBuilder result = new StringBuilder();
        for( int i = 0; i < length; i++ )
        {
            result.Append( _random.Next( 0, 10 ).ToString() ); // 0 to 9
        }
        return result.ToString();
    }

    static string GenerateRandomIP()
    {
        return $"{_random.Next( 0, 256 )}.{_random.Next( 0, 256 )}.{_random.Next( 0, 256 )}.{_random.Next( 0, 256 )}";
    }

    static string ParseVariable( string variable )
    {
        switch( variable.ToLower() )
        {
            case "$randstr":
                return GenerateRandomString( 6 );  // 6 is the length of the random string

            case "$randnum":
                return GenerateRandomNumber( 5 );  // 5 is the length of the random number

            case "$randip":
                return GenerateRandomIP();

            case "$randhost":
                return GenerateRandomHostname( "com" );

            case "$randemail":
                return GenerateRandomEmail( GenerateRandomHostname( "net" ) );

            default:
                throw new NotImplementedException();
        }
    }

    static string VariableReplacementMatchEvaluator( Match match )
    {
        return ( ParseVariable( match.Value ) );
    }

    static string ParseReplacement( string replacement )
    {
        var regex = new Regex( @"\$[a-zA-Z]+" );
        return regex.Replace( replacement, VariableReplacementMatchEvaluator );
    }


    static void ShowHelp()
    {
        Console.WriteLine( "Usage:" );
        Console.WriteLine( "LogAnonymizer [-c/--config config_filename] [-o/--output output_filename] -i/--input input_filename" );
        Console.WriteLine( "If no output_filename is specified, output is to console." );
        Console.WriteLine( "Example:" );
        Console.WriteLine( "LogAnonymizer --config LogAnonymizer.yaml --input private.log -o anon.log" );
    }

    public static void RunMain( Options opts )
    {

        //if( opts.ShowHelp )
        //{
        //    var parser = new Parser( with => with.HelpWriter = null );
        //    var notParsedResult = parser.ParseArguments<Options>( new[] { "--non-existent-arg" } );

        //    var helpText = HelpText.AutoBuild(
        //        notParsedResult,
        //        ( HelpText current ) => current,
        //        e => e
        //    );

        //    // Customize the header here too.
        //    helpText.Heading = "LogAnonymizer 1.0.0 - Copyright (C) 2023 LogZilla";

        //    Console.WriteLine( helpText );
        //    return;
        //}

        if( !string.IsNullOrEmpty( opts.ConfigFilename ) )
        {
            // Handle config file
            Console.WriteLine( $"Using config: {opts.ConfigFilename}" );
        }

        if( !string.IsNullOrEmpty( opts.InputFilename ) )
        {
            // Handle output file
            Console.WriteLine( $"Reading from: {opts.InputFilename}" );
        }
        if( !string.IsNullOrEmpty( opts.OutputFilename ) )
        {
            // Handle output file
            Console.WriteLine( $"Outputting to: {opts.OutputFilename}" );
        }

        var yamlContent = File.ReadAllText( opts.ConfigFilename );

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention( CamelCaseNamingConvention.Instance )
            .Build();

        var result = deserializer.Deserialize<ConcurrentDictionary<string, string>>( yamlContent );

        foreach( var pair in result )
        {
            if( pair.Key.Contains( @"\" ) )
            {
                _exp_replacements.TryAdd( pair.Key, pair.Value );
            }
            else
            {
                _static_replacements.TryAdd( pair.Key, pair.Value );
            }
        }

        var static_regex_str = string.Join( "|", _static_replacements.Keys );
        var static_regex = new Regex( static_regex_str, RegexOptions.Compiled );
        var exp_replacement_str_builder = new StringBuilder();
        var alphaSequenceIterator = GenerateAlphaSequence( 5 ).GetEnumerator();
        foreach( var pair in _exp_replacements )
        {
            if( exp_replacement_str_builder.Length > 0 )
            {
                exp_replacement_str_builder.Append( "|" );
            }
            alphaSequenceIterator.MoveNext();
            string tag = alphaSequenceIterator.Current;

            exp_replacement_str_builder.Append( string.Format( "(?<{0}>{1})", tag, pair.Key ) );
            _iterated_replacements.TryAdd( tag, pair.Value );
        }
        var exp_regex = new Regex( exp_replacement_str_builder.ToString(), RegexOptions.Compiled );


        ConcurrentQueue<string> outputLines = new ConcurrentQueue<string>();

        Parallel.ForEach( File.ReadLines( opts.InputFilename ), line =>
        {
            string replaced_line = static_regex.Replace( line, StaticMatchEvaluatorFunction );
            replaced_line = exp_regex.Replace( replaced_line, ExpressionMatchEvaluatorFunction );
            outputLines.Enqueue( replaced_line );
        } );

        // Write the results to the output file or console
        TextWriter tw = opts.OutputFilename != null ? new StreamWriter( opts.OutputFilename ) : Console.Out;
        int counter = 0;
        while( outputLines.TryDequeue( out var outputLine ) )
        {
            if( ++counter % 100000 == 0 )
            {
                Console.Write( "." );
            }
            tw.WriteLine( outputLine );
        }

        if( opts.OutputFilename != null )
        {
            tw.Close();
        }

        Console.WriteLine( "\nDone." );

    }

    public static void Main( string[] args )
    {
        var parser = new Parser( with => with.HelpWriter = null );
        var parsedResult = parser.ParseArguments<Options>( args );
        parsedResult
            .WithParsed( opts => RunMain( opts ) )
            .WithNotParsed( errs => HandleParseError( parsedResult, errs ) );
    }

    static void HandleParseError( ParserResult<Options> result, IEnumerable<Error> errs )
    {
        var helpText = HelpText.AutoBuild( result,
            ( HelpText current ) => HelpText.DefaultParsingErrorsHandler( result, current ),
            e => e
        );

        // Here's where you can customize the header.
        helpText.Heading = "LogAnonymizer 1.0.0 - Copyright (C) 2023 LogZilla";

        Console.WriteLine( helpText );
    }

}