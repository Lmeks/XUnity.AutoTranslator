﻿using System;
using System.Linq;
using System.Text;

namespace XUnity.AutoTranslator.Plugin.Core.Endpoints.Www
{
   internal class WwwTranslationContext : IWwwTranslationContext, IWwwRequestCreationContext, IWwwTranslationExtractionContext
   {
      private readonly ITranslationContext _context;

      internal WwwTranslationContext( ITranslationContext context )
      {
         _context = context;
      }

      public string UntranslatedText => _context.UntranslatedText;
      public string SourceLanguage => _context.SourceLanguage;
      public string DestinationLanguage => _context.DestinationLanguage;

      public string ResponseData { get; internal set; }
      internal WwwRequestInfo RequestInfo { get; private set; }

      void IWwwRequestCreationContext.Complete( WwwRequestInfo requestInfo )
      {
         RequestInfo = requestInfo;
      }

      void IWwwTranslationExtractionContext.Complete( string translatedText )
      {
         _context.Complete( translatedText );
      }

      public void Fail( string reason, Exception exception )
      {
         _context.Fail( reason, exception );
      }
   }
}
