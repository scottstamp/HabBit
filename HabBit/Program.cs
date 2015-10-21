using System;
using System.IO;
using System.Collections.Generic;

using FlashInspect;
using FlashInspect.IO;
using FlashInspect.Tags;
using FlashInspect.ActionScript;
using FlashInspect.ActionScript.Traits;
using FlashInspect.ActionScript.Constants;

namespace HabBit
{
    public class Program
    {
        static bool IsCustomE { get; set; }
        static bool IsCustomN { get; set; }
        static string FileName { get; set; }
        static string FileDirectory { get; set; }
        static IList<DoABCTag> ABCTags { get; set; }
        static ShockwaveFlash HabboClient { get; set; }

        static string Exponenet { get; set; } = "3";
        static string Modulus { get; set; } = "86851dd364d5c5cece3c883171cc6ddc5760779b992482bd1e20dd296888df91b33b936a7b93f06d29e8870f703a216257dec7c81de0058fea4cc5116f75e6efc4e9113513e45357dc3fd43d4efab5963ef178b78bd61e81a14c603b24c8bcce0a12230b320045498edc29282ff0603bc7b7dae8fc1b05b52b2f301a9dc783b7";

        static bool DisableRC4 { get; set; } = false;
        static bool DumpHeaders { get; set; } = false;
        static bool CompressClient { get; set; } = true;

        static void Main(string[] args)
        {
            Console.Title = "HabBit ~ Processing Arguments";

            HandleArguments(args);
            FileDirectory = Path.GetDirectoryName(HabboClient.Location);
            FileName = Path.GetFileNameWithoutExtension(HabboClient.Location);

            UpdateConsoleTitle();
            if (!Decompress(HabboClient))
            {
                WriteLine("Failed to decompress flash client, aborting...");
                return;
            }
            ABCTags = GetABCTags(HabboClient);

            // This always seems to be in the first .abc block.
            SanitizeClientUnloader(ABCTags[0]);

            // This method appears in a class named "Habbo", which is in "frame1".
            SanitizeIsValidHabboDomain(ABCTags[1]);

            // These methods are in "frame2"("HabboCommunicationDemo", "RC4").
            if (DisableRC4) SanitizeRC4Methods(ABCTags[2]);
            SanitizeRSAKeys(ABCTags[2]);

            if (DumpHeaders)
                ExtractMessages(ABCTags[2]);

            WriteLine("Reconstructing...");
            HabboClient.Reconstruct();

            byte[] reconstructed = CompressClient ?
                Compress(HabboClient) : HabboClient.ToArray();

            string newFilePath = $"{FileDirectory}\\CLEANED_{FileName}.swf";
            File.WriteAllBytes(newFilePath, reconstructed);

            WriteLine("Finished! | File has been saved at: " + newFilePath);
            Console.Read();
        }

        static void InsertEarlyReturnFalse(ASMethod method)
        {
            method.Body.Code.InsertInstruction(0, OPCode.PushTrue);
            method.Body.Code.InsertInstruction(1, OPCode.ReturnValue);

            WriteLine("Inserted <PushTrue>, and <ReturnValue> instructions at: {0}",
                GenerateReadableSignature(method));
        }
        static string GenerateReadableSignature(ASMethod method)
        {
            string signature = method.ObjName + "(";
            for (int i = 0; i < method.Parameters.Count; i++)
            {
                ASParameter param = method.Parameters[i];
                string paramName = "arg" + (i + 1);

                if (param.Type != null)
                    paramName += (":" + param.Type.ObjName);

                if (param.IsOptional)
                {
                    paramName += "=";
                    bool isString = (param.Type?.ObjName.ToLower() == "string");

                    if (isString) paramName += "\"";
                    object optionalValue = (param.Value ?? "null");

                    paramName += (!isString ?
                        optionalValue.ToString().ToLower() : optionalValue);

                    if (isString) paramName += "\"";
                }
                signature += paramName + ", ";
            }

            if (method.Parameters.Count > 0)
            {
                signature = signature.Remove(
                    signature.Length - 2, 2);
            }

            signature += ")";
            if (method.ReturnType != null)
                signature += (":" + method.ReturnType.ObjName);

            return signature;
        }
        static void InsertEarlyReturnLocal(ASMethod method, int local)
        {
            var getLoc = (OPCode)(local + 0xD0);

            method.Body.Code.InsertInstruction(0, getLoc);
            method.Body.Code.InsertInstruction(1, OPCode.ReturnValue);

            WriteLine("Inserted <{0}>, and <ReturnValue> instructions at: {1}",
                getLoc, GenerateReadableSignature(method));
        }

        static void ExtractMessages(DoABCTag abcTag)
        {
            ABCFile abc = abcTag.ABC;
            ASClass habboMessages = abc.FindClassByName("HabboMessages");
            var x = abc.FindClassByName("default");

            ASTrait incomingMap = habboMessages.Traits[0];
            ASTrait outgoingMap = habboMessages.Traits[1];

            int outCount = 0, inCount = 0;
            using (var mapReader = new FlashReader(
                habboMessages.Constructor.Body.Code.ToArray()))
            {
                string headerDump = string.Empty;
                while (mapReader.Position != mapReader.Length)
                {
                    var op = (OPCode)mapReader.ReadByte();
                    if (op != OPCode.GetLex) continue;

                    int multinameIndex = mapReader.Read7BitEncodedInt();

                    bool isOutgoing = (multinameIndex == outgoingMap.NameIndex);
                    bool isIncoming = (multinameIndex == incomingMap.NameIndex);
                    if (!isOutgoing && !isIncoming) continue;

                    if (isOutgoing) outCount++;
                    else inCount++;

                    op = (OPCode)mapReader.ReadByte();
                    if (op != OPCode.PushShort && op != OPCode.PushByte) continue;

                    int header = mapReader.Read7BitEncodedInt();

                    op = (OPCode)mapReader.ReadByte();
                    if (op != OPCode.GetLex) continue;

                    int messageTypeIndex = mapReader.Read7BitEncodedInt();
                    ASMultiname messageType = abc.Constants.Multinames[messageTypeIndex];

                    string title = (isOutgoing ? "Outgoing" : "Incoming");
                    headerDump += $"{title}[{header}]: {messageType.ObjName}";
                    if (isOutgoing)
                    {
                        ASInstance outgoingType =
                            abc.FindInstanceByName(messageType.ObjName);

                        headerDump +=
                            GenerateReadableSignature(outgoingType.Constructor);
                    }
                    headerDump += "\r\n";

                    // Not sure what other stuff to do with the header/message type.
                    // Maybe determine the structures? Idk.
                }

                WriteLine($"Outgoing Types: {outCount} | Incoming Types: {inCount}");
                File.WriteAllText($"{FileDirectory}\\HEADERS_{FileName}.txt", headerDump);
            }
        }
        static void SanitizeRSAKeys(DoABCTag abcTag)
        {
            ABCFile abc = abcTag.ABC;
            int modulusIndex = abc.Constants.Strings.IndexOf(Modulus);
            if (modulusIndex == -1)
            {
                abc.Constants.Strings.Add(Modulus);
                modulusIndex = (abc.Constants.Strings.Count - 1);
            }

            int exponentIndex = abc.Constants.Strings.IndexOf(Exponenet);
            if (exponentIndex == -1)
            {
                abc.Constants.Strings.Add(Exponenet);
                exponentIndex = (abc.Constants.Strings.Count - 1);
            }

            ASInstance commClass = abc.FindInstanceByName("HabboCommunicationDemo");
            foreach (ASTrait trait in commClass.Traits)
            {
                if (trait.TraitType != TraitType.Method) continue;
                var commMethod = ((MethodGetterSetterTrait)trait.Data).Method;

                if (commMethod.ReturnType.ObjName != "void") continue;
                if (commMethod.Parameters.Count != 1) continue;
                // The parameter type's <RealName> property will change in every release, so don't check it.

                ASCode methodCode = commMethod.Body.Code;
                int getlexStart = methodCode.IndexOf((byte)OPCode.GetLex);

                if (getlexStart == -1) continue;
                using (var codeReader = new FlashReader(methodCode.ToArray()))
                using (var codeWriter = new FlashWriter(codeReader.Length))
                {
                    bool searchingKeys = true;
                    while (codeReader.Position != codeReader.Length)
                    {
                        var op = (OPCode)codeReader.ReadByte();
                        codeWriter.Write((byte)op);

                        if (op != OPCode.GetLex || !searchingKeys) continue;
                        getlexStart = (codeReader.Position - 1);

                        int getlexTypeIndex = codeReader.Read7BitEncodedInt();
                        codeWriter.Write7BitEncodedInt(getlexTypeIndex);

                        int getlexSize = (codeReader.Position - getlexStart);
                        ASMultiname getlexType = abc.Constants.Multinames[getlexTypeIndex];
                        if (getlexType.ObjName != "KeyObfuscator") continue;

                        op = (OPCode)codeReader.ReadByte();
                        codeWriter.Write((byte)op);

                        if (op != OPCode.CallProperty) continue;

                        // Don't write these values, this is where our <pushstring> instruction will go.
                        int propIndex = codeReader.Read7BitEncodedInt();
                        int propArgCount = codeReader.Read7BitEncodedInt();
                        codeWriter.Position -= (getlexSize + 1); // Roll back position in <codeWriter> so we can overwrite the <getlex> instruction.

                        ASMultiname propType = abc.Constants.Multinames[propIndex];
                        int indexToPush = (modulusIndex > 0 ? modulusIndex : exponentIndex);

                        codeWriter.Write((byte)OPCode.PushString);
                        codeWriter.Write7BitEncodedInt(indexToPush);

                        WriteLine("Replaced <GetLex>, and <CallProperty>(KeyObfuscator.{0}()) with <PushString> at: {1}",
                            propType.ObjName, GenerateReadableSignature(commMethod));

                        if (modulusIndex > 0) modulusIndex = -1;
                        else searchingKeys = false;
                    }

                    methodCode.Clear();
                    methodCode.AddRange(codeWriter.ToArray());
                    if (!searchingKeys) break;
                }
            }
        }
        static void SanitizeRC4Methods(DoABCTag abcTag)
        {
            ABCFile abc = abcTag.ABC;
            ASInstance rc4Instance = abc.FindInstanceByName("RC4");
            foreach (ASTrait trait in rc4Instance.Traits)
            {
                if (trait.TraitType != TraitType.Method) continue;
                var rc4Method = ((MethodGetterSetterTrait)trait.Data).Method;

                if (rc4Method.ReturnType.ObjName != "ByteArray") continue;
                if (rc4Method.Parameters.Count != 1) continue;
                if (rc4Method.Parameters[0].Type.ObjName != "ByteArray") continue;

                InsertEarlyReturnLocal(rc4Method, 1);
            }
        }
        static void SanitizeClientUnloader(DoABCTag abcTag)
        {
            ABCFile abc = abcTag.ABC;
            ASMethod possibleDomainChecker = abc.Classes[0]
                .FindMethod("*", "Boolean");

            if (possibleDomainChecker.Parameters.Count != 1) return;
            if (possibleDomainChecker.Parameters[0].Type.ObjName != "MovieClip") return;

            InsertEarlyReturnFalse(possibleDomainChecker);
        }
        static void SanitizeIsValidHabboDomain(DoABCTag abcTag)
        {
            ABCFile abc = abcTag.ABC;
            ASClass habboClass = abc.FindClassByName("Habbo");
            ASMethod isValidHabboDomainMethod = habboClass.FindMethod("isValidHabboDomain", "Boolean");

            if (isValidHabboDomainMethod.Parameters.Count != 1) return;
            if (isValidHabboDomainMethod.Parameters[0].Type.ObjName != "String") return;

            InsertEarlyReturnFalse(isValidHabboDomainMethod);
        }

        static byte[] Compress(ShockwaveFlash flash)
        {
            WriteLine("Compressing... | ({0}MB) | This may take a while, no more than a minute, hopefully.",
                (((int)flash.FileLength / 1024) / 1024));

            return flash.Compress();
        }
        static bool Decompress(ShockwaveFlash flash)
        {
            if (flash.CompressWith == CompressionStandard.ZLIB)
            {
                int compressedSizeMB = ((flash.ToArray().Length / 1024) / 1024);
                int uncompressedSizeMB = (((int)flash.FileLength / 1024) / 1024);

                ClearWriteLine("Decompressing... | ({0}MB) -> ({1}MB)",
                    compressedSizeMB, uncompressedSizeMB);

                flash.Decompress();
            }

            return !flash.IsCompressed;
        }
        static IList<DoABCTag> GetABCTags(ShockwaveFlash flash)
        {
            WriteLine("Disassembling...");
            flash.ReadTags();

            var abcTags = new List<DoABCTag>(5);
            foreach (FlashTag tag in flash.Tags)
            {
                if (tag.Header.TagType != FlashTagType.DoABC) continue;
                abcTags.Add((DoABCTag)tag);
            }

            WriteLine("Found {0} <{1}> objects!",
                abcTags.Count, nameof(DoABCTag));

            return abcTags;
        }

        static string RequestFile()
        {
            do
            {
                Console.Clear();
                Console.Write("Habbo Client Location: ");

                string path = Console.ReadLine();
                if (string.IsNullOrEmpty(path)) continue;

                path = Path.GetFullPath(path);
                if (path.EndsWith(".swf"))
                {
                    Console.WriteLine("---------------");
                    return path;
                }
            }
            while (true);
        }
        static void UpdateConsoleTitle()
        {
            string title = "HabBit ~ ";
            title += $"{nameof(DisableRC4)}: {DisableRC4} | ";
            title += $"{nameof(CompressClient)}: {CompressClient}";

            if (IsCustomE)
                title += $" | <Custom Exponenet>";

            if (IsCustomN)
                title += $" | <Custom Modulus>";

            Console.Title = title;
        }
        static bool AskQuestion(string message)
        {
            Console.Write(message + " (Y/N): ");

            bool isValidAnwser;
            ConsoleKey pressedKey;
            do
            {
                pressedKey = Console.ReadKey().Key;

                isValidAnwser = (pressedKey == ConsoleKey.Y ||
                    pressedKey == ConsoleKey.N);

                if (!isValidAnwser) Console.Write("\b");
            }
            while (!isValidAnwser);

            WriteLine();
            return pressedKey == ConsoleKey.Y;
        }
        static void HandleArguments(string[] args)
        {
            if (args.Length < 1)
            {
                string commands = string.Empty;
                commands += RequestFile();

                if (AskQuestion("Would you like to disable RC4 encryption?"))
                    commands += "\ndisablerc4";

                string modStart = Modulus.Substring(0, 25);
                string modEnd = Modulus.Substring(Modulus.Length - 25);
                if (!AskQuestion($"E: {Exponenet}\r\nN: {modStart}...{modEnd}\r\nWould you like to use these RSA keys as replacements?"))
                {
                    Console.Write("Exponenet(E): ");
                    commands += ("\ne:" + Console.ReadLine());

                    Console.Write("Modulus(N): ");
                    commands += ("\nn:" + Console.ReadLine());

                    Console.WriteLine("---------------");
                }

                if (!AskQuestion("Would you like to compress the file once reconstruction is finished?"))
                    commands += "\nskipcompress";

                if (AskQuestion("Example: Outgoing[4000] = _-AB\r\nWould you like to dump a file containing a list of headers paired with their associated type/handler?"))
                    commands += "\ndumpheaders";

                args = commands.Split('\n');
                Console.Clear();
            }

            HabboClient = new ShockwaveFlash(args[0]);
            for (int i = 1; i < args.Length; i++)
            {
                string[] values = args[i].Split(':');
                switch (values[0].ToLower())
                {
                    case "e":
                    {
                        string e = values[1];
                        if (e == Exponenet) break;

                        Exponenet = e;
                        IsCustomE = true;
                        break;
                    }

                    case "n":
                    {
                        string n = values[1];
                        if (n == Modulus) break;

                        Modulus = n;
                        IsCustomN = true;
                        break;
                    }

                    case "disablerc4":
                    DisableRC4 = true;
                    break;

                    case "skipcompress":
                    CompressClient = false;
                    break;

                    case "dumpheaders":
                    DumpHeaders = true;
                    break;
                }
            }
        }

        static void WriteLine()
        {
            WriteLine(string.Empty);
        }
        static void ClearWriteLine()
        {
            ClearWriteLine(string.Empty);
        }

        static void WriteLine(string value)
        {
            value = (value.Trim() + "\r\n---------------");
            Console.WriteLine(value);
        }
        static void ClearWriteLine(string value)
        {
            Console.Clear();
            WriteLine(value);
        }

        static void WriteLine(string format, params object[] args)
        {
            WriteLine(string.Format(format, args));
        }
        static void ClearWriteLine(string format, params object[] args)
        {
            ClearWriteLine(string.Format(format, args));
        }
    }
}