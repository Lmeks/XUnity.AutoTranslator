﻿using System;
using System.Collections;
using System.Linq;
using XUnity.AutoTranslator.Plugin.Core.Configuration;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;
using XUnity.AutoTranslator.Plugin.Core.Web;

namespace ReverseTranslator
{
   public class ReverseTranslatorEndpoint : ITranslateEndpoint
   {
      public string Id => "Reverser";

      public string FriendlyName => "Reverser";

      public int MaxConcurrency => 50;

      public void Initialize( IInitializationContext context )
      {

      }

      public IEnumerator Translate( ITranslationContext context )
      {
         var reversedText = new string( context.UntranslatedText.Reverse().ToArray() );
         context.Complete( reversedText );

         return null;
      }
   }
}
