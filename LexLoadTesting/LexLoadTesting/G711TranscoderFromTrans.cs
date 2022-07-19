using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace icRestLib
{
    /// <summary>
    /// Used to convert between G711 8khz 8 bit samples and linear PCM 16 bit
    /// 
    /// This is needed due to the fact that AWS LEX does not support telephony standard g711 ulaw codec.
    /// </summary>
    public static class G711TranscoderFromTrans
    {
        /// <summary>
        /// inContact wav header size per WAVEFORMATEX
        /// </summary>
        static public int inContactWavFileHeaderSize = 46;

        /// <summary>
        /// ASSume that "fileName" is an accessible, valid g711 ulaw 8khz 8 bit mono file with a valid wav header
        /// Return raw linear PCM 16 bit mono memory stream (no header) 8khz
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public static MemoryStream GetLinearPcmMemoryStreamFromG711File(string fileName)
        {
            FileStream sourceFile = new FileStream(fileName, FileMode.Open, FileAccess.Read);
            MemoryStream linear = new MemoryStream();

            sourceFile.Seek(inContactWavFileHeaderSize, SeekOrigin.Begin); //seek past the extended wav header (WaveFormatEx), to the raw audio data
            for (int i = 0; i < sourceFile.Length - inContactWavFileHeaderSize; i++)
            {
                byte muLawSample = (byte)sourceFile.ReadByte();

                short linearSample = decode(muLawSample);

                byte[] bytes = BitConverter.GetBytes(linearSample);

                foreach (byte halfSample in bytes)
                {
                    linear.WriteByte(halfSample);
                }
            }
            sourceFile.Close();

            // Reset the resulting memory stream to the beginning
            linear.Seek(0, SeekOrigin.Begin);

            return linear;
        }

        /// <summary>
        /// Decode one mu-law byte. For internal use only.
        /// </summary>
        /// <param name="mulaw">The encoded mu-law byte</param>
        /// <returns>A short containing the 16-bit result</returns>
        private static short decode(byte mulaw)
        {
            //Flip all the bits
            mulaw = (byte)~mulaw;

            //Pull out the value of the sign bit
            int sign = mulaw & 0x80;
            //Pull out and shift over the value of the exponent
            int exponent = (mulaw & 0x70) >> 4;
            //Pull out the four bits of data
            int data = mulaw & 0x0f;

            //Add on the implicit fifth bit (we know the four data bits followed a one bit)
            data |= 0x10;
            /* Add a 1 to the end of the data by shifting over and adding one.  Why?
             * Mu-law is not a one-to-one function.  There is a range of values that all
             * map to the same mu-law byte.  Adding a one to the end essentially adds a
             * "half byte", which means that the decoding will return the value in the
             * middle of that range.  Otherwise, the mu-law decoding would always be
             * less than the original data. */
            data <<= 1;
            data += 1;
            /* Shift the five bits to where they need to be: left (exponent + 2) places
             * Why (exponent + 2) ?
             * 1 2 3 4 5 6 7 8 9 A B C D E F G
             * . 7 6 5 4 3 2 1 0 . . . . . . . <-- starting bit (based on exponent)
             * . . . . . . . . . . 1 x x x x 1 <-- our data
             * We need to move the one under the value of the exponent,
             * which means it must move (exponent + 2) times
             */
            data <<= exponent + 2;
            //Remember, we added to the original, so we need to subtract from the final
            data -= 0x84;
            //If the sign bit is 0, the number is positive. Otherwise, negative.
            return (short)(sign == 0 ? data : -data);
        }

        private static byte encode(int pcm) //16-bit
        {
            //Get the sign bit. Shift it for later 
            //use without further modification
            int sign = (pcm & 0x8000) >> 8;
            //If the number is negative, make it 
            //positive (now it's a magnitude)
            if (sign != 0)
                pcm = -pcm;
            //The magnitude must be less than 32635 to avoid overflow
            if (pcm > 32635) pcm = 32635;
            //Add 132 to guarantee a 1 in 
            //the eight bits after the sign bit
            pcm += 0x84;

            /* Finding the "exponent"
            * Bits:
            * 1 2 3 4 5 6 7 8 9 A B C D E F G
            * S 7 6 5 4 3 2 1 0 . . . . . . .
            * We want to find where the first 1 after the sign bit is.
            * We take the corresponding value from
            * the second row as the exponent value.
            * (i.e. if first 1 at position 7 -> exponent = 2) */
            int exponent = 7;
            //Move to the right and decrement exponent until we hit the 1
            for (int expMask = 0x4000; (pcm & expMask) == 0;
                 exponent--, expMask >>= 1) { }

            /* The last part - the "mantissa"
            * We need to take the four bits after the 1 we just found.
            * To get it, we shift 0x0f :
            * 1 2 3 4 5 6 7 8 9 A B C D E F G
            * S 0 0 0 0 0 1 . . . . . . . . . (meaning exponent is 2)
            * . . . . . . . . . . . . 1 1 1 1
            * We shift it 5 times for an exponent of two, meaning
            * we will shift our four bits (exponent + 3) bits.
            * For convenience, we will actually just shift
            * the number, then and with 0x0f. */
            int mantissa = (pcm >> (exponent + 3)) & 0x0f;

            //The mu-law byte bit arrangement 
            //is SEEEMMMM (Sign, Exponent, and Mantissa.)
            byte mulaw = (byte)(sign | exponent << 4 | mantissa);

            //Last is to flip the bits
            return (byte)~mulaw;
        }


        /// <summary>
        /// Write a standard wav hear to the stream, to preceed raw audio
        /// </summary>
        static private void WritePcm16bit8khzWavHeader(
            BinaryWriter writer,
            UInt32 numberOfPacketBytes)
        {
            UInt32 file_length = numberOfPacketBytes + 12 /* RIFF header */ + 8 /* fmt header */ + 18 /* fmt data */ + 8 /* data header */;

            // Write Data Length Header Information
            writer.Write(new char[4] { 'R', 'I', 'F', 'F' });
            writer.Write(file_length - 8);                          // Chunk size (4 bytes)
            writer.Write(new char[4] { 'W', 'A', 'V', 'E' });        // Chunk ID (i.e., chunk format) (4 bytes) 

            // Format sub-chunk
            writer.Write(new char[4] { 'f', 'm', 't', ' ' });       // Sub-chunk1 ID (4 bytes)
            writer.Write((int)18);                                  // extended format Sub-chunk Size (4 bytes)
            writer.Write((short)1);                                 // Audio Format - mu-law (2 bytes)  ("7" for g711 ulaw)
            writer.Write((short)1);                                 // Number of channels (2 bytes)
            writer.Write((int)1600);                                // Sample Rate (4 bytes)
            writer.Write((int)1600 * 1);                            // Byte Rate (4 bytes)
            writer.Write((short)(1 * 1));                           // Block Align (2 byte)
            writer.Write((short)16);                                 // Bits Per Sample (2 bytes)
            writer.Write((short)0);                                 // Extension size (no extension fields) (2 bytes)

            // Data sub-chunk
            writer.Write(new char[4] { 'd', 'a', 't', 'a' });       // Sub-chunk 2 ID 
            writer.Write(numberOfPacketBytes);                      // Sub chunk length (4 bytes)
        }

        /// <summary>
        /// Write a standard wav hear to the stream, to preceed raw audio
        /// </summary>
        static private void WriteG711ulaw8Bit8khzWavHeader(
            BinaryWriter writer,
            UInt32 numberOfPacketBytes)
        {
            UInt32 file_length = numberOfPacketBytes + 12 /* RIFF header */ + 8 /* fmt header */ + 18 /* fmt data */ + 8 /* data header */;

            // Write Data Length Header Information
            writer.Write(new char[4] { 'R', 'I', 'F', 'F' });
            writer.Write(file_length - 8);                          // Chunk size (4 bytes)
            writer.Write(new char[4] { 'W', 'A', 'V', 'E' });        // Chunk ID (i.e., chunk format) (4 bytes) 

            // Format sub-chunk
            writer.Write(new char[4] { 'f', 'm', 't', ' ' });       // Sub-chunk1 ID (4 bytes)
            writer.Write((int)18);                                  // extended format Sub-chunk Size (4 bytes)
            writer.Write((short)7);                                 // Audio Format - mu-law (2 bytes)  ("7" for g711 ulaw)
            writer.Write((short)1);                                 // Number of channels (2 bytes)
            writer.Write((int)8000);                                // Sample Rate (4 bytes)
            writer.Write((int)8000 * 1);                            // Byte Rate (4 bytes)
            writer.Write((short)(1 * 1));                           // Block Align (2 byte)
            writer.Write((short)8);                                 // Bits Per Sample (2 bytes)
            writer.Write((short)0);                                 // Extension size (no extension fields) (2 bytes)

            // Data sub-chunk
            writer.Write(new char[4] { 'd', 'a', 't', 'a' });       // Sub-chunk 2 ID 
            writer.Write(numberOfPacketBytes);                      // Sub chunk length (4 bytes)
        }


        public static MemoryStream GetPcm16Linear800KhzWavFile(string g711ulawFile)
        {
            MemoryStream audioportion = GetLinearPcmMemoryStreamFromG711File(g711ulawFile);

            MemoryStream newStream = new MemoryStream();

            BinaryWriter bw = new BinaryWriter(newStream);

            WritePcm16bit8khzWavHeader(bw, (UInt32)audioportion.Length);

            audioportion.CopyTo(newStream);

            return newStream;
        }


        /// <summary>
        /// Given that the source stream is 16 bit linear PCM, create a G711ulaw Wav file
        /// (This is used to convert AWS Polly HTTP reponse data to a wav file played natively over telecom network)
        /// </summary>
        /// <param name="sourcePcmStream"></param>
        /// <param name="destg711File"></param>
        public static void transcode16bitLPCM16khzToG7118khz(
            Stream sourcePcmStream,
          out MemoryStream destg711File)
        {
            destg711File = new MemoryStream();
            #region Find number of samples

            // The AWS response object's "ContentLength" is not supplied from the sdk :(
            // The stream does not support length or seek operations
            // Copy the PollyStream to a workable memory stream

            MemoryStream memstream = new MemoryStream();
            sourcePcmStream.CopyTo(memstream);
            long numberOfSamples = memstream.Length / 2; //2 bytes per 16 bit sample
            memstream.Seek(0, SeekOrigin.Begin); // Seek to the beginning of the stream
            #endregion

            #region Transcode PCM linear 16 bit to G711 wav file on disk

            //FileStream saved_file = new FileStream(destg711File, FileMode.Create);
            BinaryWriter bw = new BinaryWriter(destg711File);

            BinaryReader br = new BinaryReader(memstream);

            WriteG711ulaw8Bit8khzWavHeader(bw, (UInt32)numberOfSamples);

            for (long i = 0; i < numberOfSamples; i++)
            {
                int sample = br.ReadInt16();

                if (i % 2 == 0) // only convert every OTHER sample, because Lex is returning at 16000 sample rate and we only want half of it
                {
                    byte g711Sample = encode(sample);

                    bw.Write(g711Sample);
                }
            }

            bw.Close();
            #endregion
        }

        public static MemoryStream GetPcm16Linear8KhzFromG711Stream(MemoryStream sourceFile)
        {
            MemoryStream linear = new MemoryStream();
            sourceFile.Seek(inContactWavFileHeaderSize, SeekOrigin.Begin); //seek past the extended wav header (WaveFormatEx), to the raw audio data
            for (int i = 0; i < sourceFile.Length - inContactWavFileHeaderSize; i++)
            {
                byte muLawSample = (byte)sourceFile.ReadByte();
                short linearSample = decode(muLawSample);
                byte[] bytes = BitConverter.GetBytes(linearSample);
                foreach (byte halfSample in bytes)
                {
                    linear.WriteByte(halfSample);
                }
            }
            sourceFile.Close();
            // Reset the resulting memory stream to the beginning
            linear.Seek(0, SeekOrigin.Begin);
            MemoryStream newStream = new MemoryStream();
            BinaryWriter bw = new BinaryWriter(newStream);
            WritePcm16bit8khzWavHeader(bw, (UInt32)linear.Length);
            linear.CopyTo(newStream);
            return newStream;
        }
    }
}
