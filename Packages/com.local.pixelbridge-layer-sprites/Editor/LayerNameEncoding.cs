using System;
using System.Text;

namespace PixelBridge.LayerSprites
{
    internal static class LayerNameEncoding
    {
        // The manifest carries ASCII hex so JSON/console locale cannot reinterpret Korean bytes.
        public static string DecodeUtf8Hex(string hex)
        {
            if(hex==null || hex.Length%2!=0) throw new ArgumentException("Invalid layer name byte encoding.");
            var bytes=new byte[hex.Length/2];
            for(int i=0;i<bytes.Length;i++) bytes[i]=(byte)((Digit(hex[2*i])<<4)|Digit(hex[2*i+1]));
            return new UTF8Encoding(false,true).GetString(bytes);
        }
        private static int Digit(char value)
        {
            if(value>='0' && value<='9') return value-'0';
            if(value>='a' && value<='f') return value-'a'+10;
            if(value>='A' && value<='F') return value-'A'+10;
            throw new ArgumentException("Invalid hex digit in layer name metadata.");
        }
    }
}
