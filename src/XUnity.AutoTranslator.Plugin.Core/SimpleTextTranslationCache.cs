﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Utilities;
using XUnity.Common.Logging;

namespace XUnity.AutoTranslator.Plugin.Core
{
   /// <summary>
   /// This is a simplfied version of the text cache this plugin
   /// uses to store translations.
   /// </summary>
   public class SimpleTextTranslationCache
   {
      private static readonly char[] TranslationSplitters = new char[] { '=' };

      private Dictionary<string, string> _translations = new Dictionary<string, string>();
      private List<RegexTranslation> _defaultRegexes = new List<RegexTranslation>();
      private HashSet<string> _registeredRegexes = new HashSet<string>();

      private static object _writeToFileSync = new object(); // static on purpose so we do not start 100 IO operations at the same time
      private Dictionary<string, string> _newTranslations = new Dictionary<string, string>();
      private Coroutine _currentScheduledTask;
      private bool _shouldOverrideEntireFile;

      /// <summary>
      /// Creates a translation cache and loads the translations in the specified file.
      /// </summary>
      /// <param name="file">This is the file containing the translations to be loaded.</param>
      /// <param name="loadTranslationsInFile">This is a bool indicating if the translations in the file specified should be loaded if possible.</param>
      public SimpleTextTranslationCache( string file, bool loadTranslationsInFile )
      {
         LoadedFile = file;

         if( loadTranslationsInFile )
         {
            LoadTranslationFiles();
         }
      }

      /// <summary>
      /// Gets the file that was used to initialize the SimpleTextTranslationCache.
      /// </summary>
      public string LoadedFile { get; }

      internal void LoadTranslationFiles()
      {
         try
         {
            LoadTranslationsInFile( LoadedFile );
         }
         catch( Exception e )
         {
            XuaLogger.Default.Error( e, "An error occurred while loading translations." );
         }
      }

      private void LoadTranslationsInFile( string fullFileName )
      {
         var fileExists = File.Exists( fullFileName );
         if( fileExists )
         {
            if( fileExists )
            {
               string[] translations = File.ReadAllLines( fullFileName, Encoding.UTF8 );
               foreach( string translatioOrDirective in translations )
               {
                  string[] kvp = translatioOrDirective.Split( TranslationSplitters, StringSplitOptions.None );
                  if( kvp.Length == 2 )
                  {
                     string key = TextHelper.Decode( kvp[ 0 ] );
                     string value = TextHelper.Decode( kvp[ 1 ] );

                     if( !string.IsNullOrEmpty( key ) && !string.IsNullOrEmpty( value ) )
                     {
                        if( key.StartsWith( "r:" ) )
                        {
                           try
                           {
                              var regex = new RegexTranslation( key, value );

                              AddTranslationRegex( regex );
                           }
                           catch( Exception e )
                           {
                              XuaLogger.Default.Warn( e, $"An error occurred while constructing regex translation: '{translatioOrDirective}'." );
                           }
                        }
                        else
                        {
                           AddTranslation( key, value );
                        }
                     }
                  }
               }
            }
         }
      }

      private void AddTranslationRegex( RegexTranslation regex )
      {
         if( !_registeredRegexes.Contains( regex.Original ) )
         {
            _registeredRegexes.Add( regex.Original );
            _defaultRegexes.Add( regex );
         }
      }

      private bool HasTranslated( string key )
      {
         return _translations.ContainsKey( key );
      }

      private void AddTranslation( string key, string value )
      {
         if( key != null && value != null )
         {
            _translations[ key ] = value;
         }
      }

      /// <summary>
      /// Adds a translation to the cache and writes it to the specified file (with a small delay, on a seperate thread).
      /// </summary>
      /// <param name="key"></param>
      /// <param name="value"></param>
      public void AddTranslationToCache( string key, string value )
      {
         var hadTranslated = HasTranslated( key );

         AddTranslation( key, value );
         // how do we override an existing translation in a file?

         QueueNewTranslationForDisk( key, value, hadTranslated );
      }

      private void QueueNewTranslationForDisk( string key, string value, bool hadTranslated )
      {
         lock( _newTranslations )
         {
            _newTranslations[ key ] = value;

            if( hadTranslated )
            {
               _shouldOverrideEntireFile = true;
            }

            if( _currentScheduledTask != null )
            {
               CoroutineHelper.Stop( _currentScheduledTask );
            }
            _currentScheduledTask = CoroutineHelper.Start( ScheduleFileWriting() );
         }
      }

      private IEnumerator ScheduleFileWriting()
      {
         yield return new WaitForSeconds( 1 );

         _currentScheduledTask = null;
         ThreadPool.QueueUserWorkItem( SaveNewTranslationsToDisk );
      }

      internal void SaveNewTranslationsToDisk( object state )
      {
         bool overwriteFile = false;
         if( _newTranslations.Count > 0 )
         {
            Dictionary<string, string> newTranslations;
            lock( _newTranslations )
            {
               newTranslations = _newTranslations.ToDictionary( x => x.Key, x => x.Value );
               _newTranslations.Clear();

               if( _shouldOverrideEntireFile )
               {
                  overwriteFile = true;
                  _shouldOverrideEntireFile = false;

                  lock( _translations )
                  {
                     foreach( var kvp in _translations )
                     {
                        if( !newTranslations.ContainsKey( kvp.Key ) )
                        {
                           newTranslations[ kvp.Key ] = kvp.Value;
                        }
                     }

                     foreach( var regex in _defaultRegexes )
                     {
                        if( !newTranslations.ContainsKey( regex.Key ) )
                        {
                           newTranslations[ regex.Key ] = regex.Value;
                        }
                     }
                  }
               }
            }

            lock( _writeToFileSync )
            {
               Directory.CreateDirectory( new FileInfo( LoadedFile ).Directory.FullName );

               using( var stream = File.Open( LoadedFile, overwriteFile ? FileMode.Create : FileMode.Append, FileAccess.Write ) )
               using( var writer = new StreamWriter( stream, Encoding.UTF8 ) )
               {
                  foreach( var kvp in newTranslations )
                  {
                     writer.WriteLine( TextHelper.Encode( kvp.Key ) + '=' + TextHelper.Encode( kvp.Value ) );
                  }
                  writer.Flush();
               }
            }
         }
      }

      /// <summary>
      /// Attempts to resolve a translation for the specified string.
      /// </summary>
      /// <param name="untranslatedText"></param>
      /// <param name="allowRegex"></param>
      /// <param name="value"></param>
      /// <returns></returns>
      public bool TryGetTranslation( string untranslatedText, bool allowRegex, out string value )
      {
         var key = new UntranslatedText( untranslatedText, false, true, Settings.FromLanguageUsesWhitespaceBetweenWords, false );

         bool result;
         string untemplated;
         string unmodifiedValue;
         string unmodifiedKey;

         string untemplated_TemplatedOriginal_Text = null;
         string untemplated_TemplatedOriginal_Text_InternallyTrimmed = null;
         string untemplated_TemplatedOriginal_Text_ExternallyTrimmed = null;
         string untemplated_TemplatedOriginal_Text_FullyTrimmed = null;

         lock( _translations )
         {
            // lookup UNTEMPLATED translations - ALL VARIATIONS
            if( key.IsTemplated && !key.IsFromSpammingComponent )
            {
               // key.TemplatedOriginal_Text = '   What are you \ndoing here, {{A}}?'
               // untemplated                = '   What are you \ndoing here, Sophie?'

               // THEN: Check unscoped translations
               // lookup original
               untemplated = untemplated_TemplatedOriginal_Text ?? ( untemplated_TemplatedOriginal_Text = key.Untemplate( key.TemplatedOriginal_Text ) );
               result = _translations.TryGetValue( untemplated, out value );
               if( result )
               {
                  return result;
               }

               // lookup original minus external whitespace
               if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_ExternallyTrimmed ) )
               {
                  // key.TemplatedOriginal_Text_ExternallyTrimmed = 'What are you \ndoing here, {{A}}?'
                  // untemplated                                  = 'What are you \ndoing here, Sophie?'

                  untemplated = untemplated_TemplatedOriginal_Text_ExternallyTrimmed ?? ( untemplated_TemplatedOriginal_Text_ExternallyTrimmed = key.Untemplate( key.TemplatedOriginal_Text_ExternallyTrimmed ) );
                  result = _translations.TryGetValue( untemplated, out value );
                  if( result )
                  {
                     // WHITESPACE DIFFERENCE, Store new value
                     unmodifiedValue = key.LeadingWhitespace + value + key.TrailingWhitespace;
                     unmodifiedKey = untemplated_TemplatedOriginal_Text ?? ( untemplated_TemplatedOriginal_Text = key.Untemplate( key.TemplatedOriginal_Text ) );

                     value = unmodifiedValue;
                     return result;
                  }
               }

               // lookup internally trimmed
               if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_InternallyTrimmed ) )
               {
                  // key.TemplatedOriginal_Text_InternallyTrimmed = '   What are you doing here, {{A}}?'
                  // untemplated                                  = '   What are you doing here, Sophie?'

                  untemplated = untemplated_TemplatedOriginal_Text_InternallyTrimmed ?? ( untemplated_TemplatedOriginal_Text_InternallyTrimmed = key.Untemplate( key.TemplatedOriginal_Text_InternallyTrimmed ) );
                  result = _translations.TryGetValue( untemplated, out value );
                  if( result )
                  {
                     // WHITESPACE DIFFERENCE, Store new value
                     unmodifiedValue = value;
                     unmodifiedKey = untemplated_TemplatedOriginal_Text ?? ( untemplated_TemplatedOriginal_Text = key.Untemplate( key.TemplatedOriginal_Text ) );

                     value = unmodifiedValue;
                     return result;
                  }
               }

               // lookup internally trimmed minus external whitespace
               if( !ReferenceEquals( key.TemplatedOriginal_Text_InternallyTrimmed, key.TemplatedOriginal_Text_FullyTrimmed ) )
               {
                  // key.TemplatedOriginal_Text_FullyTrimmed = 'What are you doing here, {{A}}?'
                  // untemplated                             = 'What are you doing here, Sophie?'

                  untemplated = untemplated_TemplatedOriginal_Text_FullyTrimmed ?? ( untemplated_TemplatedOriginal_Text_FullyTrimmed = key.Untemplate( key.TemplatedOriginal_Text_FullyTrimmed ) );
                  result = _translations.TryGetValue( untemplated, out value );
                  if( result )
                  {
                     // WHITESPACE DIFFERENCE, Store new value
                     unmodifiedValue = key.LeadingWhitespace + value + key.TrailingWhitespace;
                     unmodifiedKey = untemplated_TemplatedOriginal_Text ?? ( untemplated_TemplatedOriginal_Text = key.Untemplate( key.TemplatedOriginal_Text ) );

                     value = unmodifiedValue;
                     return result;
                  }
               }
            }

            // THEN: Check unscoped translations
            // key.TemplatedOriginal_Text = '   What are you \ndoing here, {{A}}?'
            result = _translations.TryGetValue( key.TemplatedOriginal_Text, out value );
            if( result )
            {
               return result;
            }

            // lookup original minus external whitespace
            if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_ExternallyTrimmed ) )
            {
               // key.TemplatedOriginal_Text_ExternallyTrimmed = 'What are you \ndoing here, {{A}}?'

               result = _translations.TryGetValue( key.TemplatedOriginal_Text_ExternallyTrimmed, out value );
               if( result )
               {
                  // WHITESPACE DIFFERENCE, Store new value
                  unmodifiedValue = key.LeadingWhitespace + value + key.TrailingWhitespace;

                  value = unmodifiedValue;
                  return result;
               }
            }

            // lookup internally trimmed
            if( !ReferenceEquals( key.TemplatedOriginal_Text, key.TemplatedOriginal_Text_InternallyTrimmed ) )
            {
               // key.TemplatedOriginal_Text_InternallyTrimmed = '   What are you doing here, {{A}}?'

               result = _translations.TryGetValue( key.TemplatedOriginal_Text_InternallyTrimmed, out value );
               if( result )
               {
                  return result;
               }
            }

            // lookup internally trimmed minus external whitespace
            if( !ReferenceEquals( key.TemplatedOriginal_Text_InternallyTrimmed, key.TemplatedOriginal_Text_FullyTrimmed ) )
            {
               // key.TemplatedOriginal_Text_FullyTrimmed = 'What are you doing here, {{A}}?'

               result = _translations.TryGetValue( key.TemplatedOriginal_Text_FullyTrimmed, out value );
               if( result )
               {
                  // WHITESPACE DIFFERENCE, Store new value
                  unmodifiedValue = key.LeadingWhitespace + value + key.TrailingWhitespace;

                  value = unmodifiedValue;
                  return result;
               }
            }

            // regex lookups - ONLY ORIGNAL VARIATION
            if( allowRegex )
            {
               for( int i = _defaultRegexes.Count - 1; i > -1; i-- )
               {
                  var regex = _defaultRegexes[ i ];
                  try
                  {
                     var match = regex.CompiledRegex.Match( key.TemplatedOriginal_Text );
                     if( !match.Success ) continue;

                     value = regex.CompiledRegex.Replace( key.TemplatedOriginal_Text, regex.Translation );

                     return true;
                  }
                  catch( Exception e )
                  {
                     _defaultRegexes.RemoveAt( i );

                     XuaLogger.Default.Error( e, $"Failed while attempting to replace or match text of regex '{regex.Original}'. Removing that regex from the cache." );
                  }
               }
            }
         }

         return result;
      }
   }
}