using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace KVMClient.Core.IPMI.UDP
{
    public class IpmiUdpClient : IIpmiInterfaceProvider
    {
        private UdpClient client = new UdpClient();
        //private IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 11000);

        private byte consoleSessionIDCount = 1;
        byte messageTag = 1;
        private byte[] consoleSessionID = new byte[] { 1, 0, 0, 0 };
        private uint sessionID = 0;
        private byte[] KG = new byte[] { unchecked((byte)-6), unchecked((byte)-71), 121, unchecked((byte)-40), 73, unchecked((byte)-66), unchecked((byte)-109), 61, unchecked((byte)-48), unchecked((byte)-60), 31, unchecked((byte)-46), unchecked((byte)-55), unchecked((byte)-41), 122, 48, unchecked((byte)-86), unchecked((byte)-115), 63, 80 };
        private byte[] SIK = null;
        private byte[] K1 = null;
        private byte[] K2 = null;

        private RMCPHeader header = new RMCPHeader();
        private SessionOpenRequest openSessionRequest = new SessionOpenRequest();
        private OpenSessionResponse openSessionResponse = new OpenSessionResponse();
        private SessionHeader sessionHeader = new SessionHeader();
        private RAKPMessage1 rakpMessage1 = new RAKPMessage1();
        private RAKPMessage2 rakpMessage2 = new RAKPMessage2();
        private RAKPMessage3 rakpMessage3 = new RAKPMessage3();
        private RAKPMessage4 rakpMessage4 = new RAKPMessage4();

        private byte authenticationAlgorithm = 1;
        private byte integrityAlgorithm = 1;
        private byte confidentialityAlgorithm = 1;
        private uint sessionSeq = 0;
        private string Username = "";
        private string Password = "";

        public static String[] RMCPPlusStatusCode = new String[] { "No errors (status code = 00h)", "Insufficient resources to create a session (status code = 01h)", "Invalid Session ID (status code = 02h)", "Invalid payload type (status code = 03h)", "Invalid authentication algorithm (status code = 04h)", "Invalid integrity algorithm (status code = 05h)", "No matching authentication payload (status code = 06h)", "No matching integrity payload (status code = 07h)", "Inactive Session ID (status code = 08h)", "Invalid role (status code = 09h)", "Unauthorized role or privilege level requested (status code = 0Ah)", "Insufficient resources to create a session at the requested role (status code = 0Bh)", "Invalid name length (status code = 0Ch)", "Unauthorized name (status code = 0Dh)", "Unauthorized GUID (status code = 0Eh). (GUID that BMC submitted in RAKP Message 2 was not accepted by remote console)", "Invalid integrity check value (status code = 0Fh)", "Invalid confidentiality algorithm (status code = 10h)", "No Cipher Suite match with proposed security algorithms (status code = 11h)", "Illegal or unrecognized parameter (status code = 12h)" };

        public async Task<bool> Authenticate(string ip, string username, string password)
        {
            Username = username;
            Password = password;
            client.Connect(IPAddress.Parse(ip), 623);

            if (await OpenSession())
            {
                var result = await setSessionPrivilegeLevelCommand(4);
                if (result.completionCode != 0)
                {
                    Console.WriteLine("Failed to elevate permissions: " + result.completionCode);
                }

                return true;
            }

            return false;
        }

        public Task<IpmiBoardInfoResult?> GetPlatformInfo()
        {
            throw new NotImplementedException();
        }

        public Task<Stream?> CapturePreview()
        {
            throw new NotImplementedException();
        }

        private async Task<IPMIMessage> setSessionPrivilegeLevelCommand(byte privilegeLevel)
        {
            IPMIMessage ipmiMessage = new IPMIMessage();
            byte[] data = new byte[] { privilegeLevel };
            ipmiMessage.setCommandAndData((byte)24, (byte)59, data);
            return await SendIpmiMessage(ipmiMessage);
        }
        private void SetIPMIMessagePayLoadTypeByCipherSuite(byte payLoadType)
        {
            if (payLoadType == 0)
            {
                this.sessionHeader.payLoadType = 0;
            }
            else if (payLoadType == 1)
            {
                this.sessionHeader.payLoadType = 1;
            }

            if (this.confidentialityAlgorithm > 0)
            {
                this.sessionHeader.payLoadType |= unchecked((byte)-128);
            }

            if (this.integrityAlgorithm > 0)
            {
                this.sessionHeader.payLoadType = (byte)(this.sessionHeader.payLoadType | 0x40);
            }

        }
        private async Task<IPMIMessage> SendIpmiMessage(IPMIMessage m)
        {
            if (m is IPMIMessage)
            {
                SetIPMIMessagePayLoadTypeByCipherSuite(0);
            }

            byte[] encryptPayLoad = this.encryptPayLoad(m);
            byte[] auth = finalSignAuthCode(encryptPayLoad);

            for (int i = 3 - 1; i >= 0; i--)
            {
                client.Send(auth);

                var result = (await client.ReceiveAsync()).Buffer;
                if (result != null)
                {
                    byte[] decrypted = this.DecryptPayload(result);
                    byte[] payloadlength = new byte[2];
                    byte[] payload = new byte[decrypted.Length - 16];
                    Array.Copy(decrypted, 14, payloadlength, 0, 2);
                    Array.Copy(decrypted, 16, payload, 0, twoBytesToInt(payloadlength));
                    IncreaseSessionSeq();
                    IPMIMessage resMessage = IPMIMessage.fromRaw(payload);

                    return resMessage;
                }
                else
                {
                    IncreaseSessionSeq();
                    encryptPayLoad = this.encryptPayLoad(m);
                    auth = finalSignAuthCode(encryptPayLoad);
                }
            }

            return null;
        }

        private byte[] DecryptPayload(byte[] recvBuf)
        {
            byte[] decrypted;
            switch (confidentialityAlgorithm)
            {
                case 0:
                    decrypted = this.DecryptPayloadNoEncryption(recvBuf);
                    break;
                case 1:
                    decrypted = this.DecryptPayloadAESCBC128(recvBuf);
                    break;
                case 2:
                    decrypted = this.DecryptPayloadXRC4128(recvBuf);
                    break;
                case 3:
                    decrypted = this.DecryptPayloadXRC440(recvBuf);
                    break;
                default:
                    throw new NotImplementedException();
            }

            return decrypted;
        }

        private byte[] DecryptPayloadXRC440(byte[] recvBuf)
        {
            throw new NotImplementedException();
        }

        private byte[] DecryptPayloadXRC4128(byte[] recvBuf)
        {
            throw new NotImplementedException();
        }

        private byte[] DecryptPayloadNoEncryption(byte[] recvBuf)
        {
            return recvBuf;
        }
        public static int twoBytesToInt(byte[] b)
        {
            int b1 = b[1] & 255;
            int b0 = b[0] & 255;
            return (b1 << 8) + b0;
        }
        private byte[] DecryptPayloadAESCBC128(byte[] recvBuf)
        {
            byte[] payLoadLength = new byte[2];
            Array.Copy(recvBuf, 14, payLoadLength, 0, 2);
            byte[] wholePayLoad = new byte[twoBytesToInt(payLoadLength)];
            Array.Copy(recvBuf, 16, wholePayLoad, 0, twoBytesToInt(payLoadLength));
            byte[] iv = new byte[16];
            byte[] encryptPayLoad = new byte[wholePayLoad.Length - 16];
            Array.Copy(wholePayLoad, 0, iv, 0, iv.Length);
            Array.Copy(wholePayLoad, iv.Length, encryptPayLoad, 0, encryptPayLoad.Length);
            byte[] decryptPayLoad = (new AesCBC128()).Decrypt(iv, K2, encryptPayLoad);
            byte[] originalPayLoad;
            if (decryptPayLoad[decryptPayLoad.Length - 1] >= 0 && decryptPayLoad[decryptPayLoad.Length - 1] <= 15)
            {
                byte padLength = decryptPayLoad[decryptPayLoad.Length - 1];
                originalPayLoad = new byte[decryptPayLoad.Length - padLength - 1];
            }
            else
            {
                originalPayLoad = new byte[decryptPayLoad.Length];
            }

            Array.Copy(decryptPayLoad, 0, originalPayLoad, 0, originalPayLoad.Length);
            byte[] originalPacket = new byte[16 + originalPayLoad.Length];
            byte[] blength = new byte[2];
            intTo2Bytes(blength, 0, originalPayLoad.Length);
            Array.Copy(recvBuf, 0, originalPacket, 0, 14);
            Array.Copy(blength, 0, originalPacket, 14, 2);
            Array.Copy(originalPayLoad, 0, originalPacket, 16, originalPayLoad.Length);
            return originalPacket;
        }
        public void IncreaseSessionSeq()
        {
            sessionSeq++;
        }
        private byte[] encryptPayLoad(IPMIMessage ipmiMessage)
        {
            byte[] encrypted;
            switch (confidentialityAlgorithm)
            {
                case 0:
                    encrypted = this.encryptPayLoad_NONE(ipmiMessage);
                    break;
                case 1:
                    encrypted = this.encryptPayLoad_AES_CBC_128(ipmiMessage);
                    break;
                case 2:
                    encrypted = this.encryptPayLoad_XRC4_128(ipmiMessage);
                    break;
                case 3:
                    encrypted = this.encryptPayLoad_XRC4_40(ipmiMessage);
                    break;
                default:
                    throw new NotImplementedException();
            }

            return encrypted;
        }

        private byte[] encryptPayLoad_NONE(IPMIMessage ipmiMessage)
        {
            byte[] payLoad = ipmiMessage.raw();
            return payLoad;
        }
        private byte[] Random16Bytes()
        {
            Random r = new Random();
            byte[] k = new byte[16];
            r.NextBytes(k);
            return k;
        }
        private byte[] encryptPayLoad_AES_CBC_128(IPMIMessage ipmiMessage)
        {
            byte[] originalPayLoad = this.encryptPayLoad_NONE(ipmiMessage);
            byte[] iv = Random16Bytes();
            int padSize = 0;
            byte[] pad = null;
            if ((originalPayLoad.Length + 1) % 16 == 0)
            {
                padSize = 0;
                pad = new byte[0];
            }
            else
            {
                padSize = 16 - (originalPayLoad.Length + 1) % 16;
                pad = new byte[padSize];

                for (int i = 0; i < padSize; ++i)
                {
                    pad[i] = (byte)(i + 1);
                }
            }

            byte[] tobeEncryptedPayLoad = new byte[originalPayLoad.Length + padSize + 1];
            Array.Copy(originalPayLoad, 0, tobeEncryptedPayLoad, 0, originalPayLoad.Length);
            Array.Copy(pad, 0, tobeEncryptedPayLoad, originalPayLoad.Length, pad.Length);
            tobeEncryptedPayLoad[originalPayLoad.Length + pad.Length] = (byte)padSize;
            byte[] encryptedPayLoad = (new AesCBC128()).Encrypt(iv, K2, tobeEncryptedPayLoad);
            byte[] AES_CBC_payLoad = new byte[iv.Length + encryptedPayLoad.Length];
            Array.Copy(iv, 0, AES_CBC_payLoad, 0, 16);
            Array.Copy(encryptedPayLoad, 0, AES_CBC_payLoad, 16, encryptedPayLoad.Length);
            return AES_CBC_payLoad;
        }

        private byte[] encryptPayLoad_XRC4_128(IPMIMessage ipmiMessage)
        {
            throw new NotImplementedException();
            //byte[] originalPayLoad = this.encryptPayLoad_NONE(ipmiMessage);
            //byte[] encryptPayLoad = null;
            //byte[] encrypt;
            //byte[] encryptPayLoad;
            //if (this.rmcpPlusSession.sessionSeq[0] == 1 && this.rmcpPlusSession.sessionSeq[1] == 0 && this.rmcpPlusSession.sessionSeq[2] == 0 && this.rmcpPlusSession.sessionSeq[3] == 0)
            //{
            //    encrypt = ByteUtility.random16Bytes();
            //    this.rmcpPlusSession.ivForEncrypt = encrypt;
            //    byte[] temp = new byte[32];
            //    System.arraycopy(this.rmcpPlusSession.K2, 0, temp, 0, 16);
            //    System.arraycopy(encrypt, 0, temp, 16, 16);
            //    this.rmcpPlusSession.keyRcForEncrypt = (new MD5_128()).code((byte[])null, temp);
            //    ByteUtility.intTo4Bytes(this.rmcpPlusSession.dataOffsetBytes, 0, 0);
            //    this.rmcpPlusSession.xrc4_128_encrypt = new XRC4_128();
            //    byte[] encrypt = this.rmcpPlusSession.xrc4_128_encrypt.encrypt(encrypt, this.rmcpPlusSession.keyRcForEncrypt, originalPayLoad);
            //    encryptPayLoad = new byte[20 + encrypt.length];
            //    System.arraycopy(this.rmcpPlusSession.dataOffsetBytes, 0, encryptPayLoad, 0, 4);
            //    System.arraycopy(encrypt, 0, encryptPayLoad, 4, 16);
            //    System.arraycopy(encrypt, 0, encryptPayLoad, 20, encrypt.length);
            //}
            //else
            //{
            //    ByteUtility.intTo4Bytes(this.rmcpPlusSession.dataOffsetBytes, 0, this.rmcpPlusSession.dataOffsetForEncrypt);
            //    encrypt = this.rmcpPlusSession.xrc4_128_encrypt.encrypt(this.rmcpPlusSession.ivForEncrypt, this.rmcpPlusSession.keyRcForEncrypt, originalPayLoad);
            //    encryptPayLoad = new byte[4 + encrypt.length];
            //    System.arraycopy(this.rmcpPlusSession.dataOffsetBytes, 0, encryptPayLoad, 0, 4);
            //    System.arraycopy(encrypt, 0, encryptPayLoad, 4, encrypt.length);
            //}

            //RMCPPlusSession var10000 = this.rmcpPlusSession;
            //var10000.dataOffsetForEncrypt += originalPayLoad.length;
            //return encryptPayLoad;
        }

        private byte[] encryptPayLoad_XRC4_40(IPMIMessage ipmiMessage)
        {
            throw new NotImplementedException();
            //byte[] originalPayLoad = this.encryptPayLoad_NONE(ipmiMessage);
            //byte[] encryptPayLoad = null;
            //byte[] encrypt;
            //byte[] encryptPayLoad;
            //if (this.rmcpPlusSession.sessionSeq[0] == 1 && this.rmcpPlusSession.sessionSeq[1] == 0 && this.rmcpPlusSession.sessionSeq[2] == 0 && this.rmcpPlusSession.sessionSeq[3] == 0)
            //{
            //    encrypt = ByteUtility.random16Bytes();
            //    this.rmcpPlusSession.ivForEncrypt = encrypt;
            //    byte[] temp = new byte[32];
            //    System.arraycopy(this.rmcpPlusSession.K2, 0, temp, 0, 16);
            //    System.arraycopy(encrypt, 0, temp, 16, 16);
            //    this.rmcpPlusSession.keyRcForEncrypt = (new MD5_128()).code((byte[])null, temp);
            //    ByteUtility.intTo4Bytes(this.rmcpPlusSession.dataOffsetBytes, 0, 0);
            //    this.rmcpPlusSession.xrc4_40_encrypt = new XRC4_40();
            //    byte[] encrypt = this.rmcpPlusSession.xrc4_40_encrypt.encrypt(encrypt, this.rmcpPlusSession.keyRcForEncrypt, originalPayLoad);
            //    encryptPayLoad = new byte[20 + encrypt.length];
            //    System.arraycopy(this.rmcpPlusSession.dataOffsetBytes, 0, encryptPayLoad, 0, 4);
            //    System.arraycopy(encrypt, 0, encryptPayLoad, 4, 16);
            //    System.arraycopy(encrypt, 0, encryptPayLoad, 20, encrypt.length);
            //}
            //else
            //{
            //    ByteUtility.intTo4Bytes(this.rmcpPlusSession.dataOffsetBytes, 0, this.rmcpPlusSession.dataOffsetForEncrypt);
            //    encrypt = this.rmcpPlusSession.xrc4_40_encrypt.encrypt(this.rmcpPlusSession.ivForEncrypt, this.rmcpPlusSession.keyRcForEncrypt, originalPayLoad);
            //    encryptPayLoad = new byte[4 + encrypt.length];
            //    System.arraycopy(this.rmcpPlusSession.dataOffsetBytes, 0, encryptPayLoad, 0, 4);
            //    System.arraycopy(encrypt, 0, encryptPayLoad, 4, encrypt.length);
            //}

            //RMCPPlusSession var10000 = this.rmcpPlusSession;
            //var10000.dataOffsetForEncrypt += originalPayLoad.length;
            //return encryptPayLoad;
        }
        private byte[] finalSignAuthCode(byte[] encryptedPayLoad)
        {
            byte[] data = null;
            switch (this.integrityAlgorithm)
            {
                case 0:
                    data = this.finalSignAuthCode_NONE(encryptedPayLoad);
                    break;
                case 1:
                    data = this.finalSignAuthCode_HMAC_SHA1_96(encryptedPayLoad);
                    break;
                case 2:
                    data = this.finalSignAuthCode_HMAC_MD5_128(encryptedPayLoad);
                    break;
                case 3:
                    data = this.finalSignAuthCode_MD5_128(encryptedPayLoad);
                    break;
            }

            return data;
        }
        private byte[] finalSignAuthCode_NONE(byte[] encryptedPayLoad)
        {
            int beforeSignPacketSize = this.header.Size() + this.sessionHeader.Size() + 2 + encryptedPayLoad.Length;
            byte[] data = new byte[beforeSignPacketSize];
            int index = 0;
            Array.Copy(this.header.GetBytes(), 0, data, 0, this.header.Size());
            index = index + this.header.Size();
            data[index++] = this.sessionHeader.AuthType;
            data[index++] = this.sessionHeader.payLoadType;
            this.sessionHeader.SessionID = this.sessionID;
            this.sessionHeader.SessionSeq = this.sessionSeq;
            Array.Copy(BitConverter.GetBytes(this.sessionHeader.SessionID), 0, data, index, 4);
            index += 4;
            Array.Copy(BitConverter.GetBytes(this.sessionHeader.SessionSeq), 0, data, index, 4);
            index += 4;
            byte[] payLoadLength = new byte[2];
            intTo2Bytes(payLoadLength, 0, encryptedPayLoad.Length);
            Array.Copy(payLoadLength, 0, data, index, payLoadLength.Length);
            index += 2;
            Array.Copy(encryptedPayLoad, 0, data, index, encryptedPayLoad.Length);
            return data;
        }

        private byte[] finalSignAuthCode_HMAC_SHA1_96(byte[] encryptedPayLoad)
        {
            byte[] tempData = this.finalSignAuthCode_NONE(encryptedPayLoad);
            int padSize = 4 - (tempData.Length + 2) % 4;
            byte[] data = new byte[tempData.Length + padSize + 2 + 12];
            Array.Copy(tempData, 0, data, 0, tempData.Length);

            for (int i = 0; i < padSize; ++i)
            {
                data[tempData.Length + i] = unchecked((byte)-1);
            }

            data[tempData.Length + padSize] = (byte)padSize;
            data[tempData.Length + padSize + 1] = 7;
            byte[] temp = new byte[tempData.Length + padSize + 2];
            Array.Copy(data, 0, temp, 0, temp.Length);
            byte[] temp2 = new byte[temp.Length - 4];
            Array.Copy(temp, 4, temp2, 0, temp2.Length);
            byte[] authCode = (new HmacSha196()).Encode(this.K1, temp2);
            if (authCode != null)
            {
                Array.Copy(authCode, 0, data, tempData.Length + padSize + 2, authCode.Length);
            }

            return data;
        }

        private byte[] finalSignAuthCode_HMAC_MD5_128(byte[] encryptedPayLoad)
        {
            throw new NotImplementedException();
            //byte[] tempData = this.finalSignAuthCode_NONE(encryptedPayLoad);
            //int padSize = 4 - (tempData.length + 2) % 4;
            //byte[] data = new byte[tempData.length + padSize + 2 + 16];
            //System.arraycopy(tempData, 0, data, 0, tempData.length);

            //for (int i = 0; i < padSize; ++i)
            //{
            //    data[tempData.length + i] = -1;
            //}

            //data[tempData.length + padSize] = (byte)padSize;
            //data[tempData.length + padSize + 1] = 7;
            //byte[] temp = new byte[tempData.length + padSize + 2];
            //System.arraycopy(data, 0, temp, 0, temp.length);
            //byte[] temp2 = new byte[temp.length - 4];
            //System.arraycopy(temp, 4, temp2, 0, temp2.length);
            //byte[] authCode = (new HMAC_MD5_128()).code(this.rmcpPlusSession.K1, temp2);
            //if (authCode != null)
            //{
            //    System.arraycopy(authCode, 0, data, tempData.length + padSize + 2, authCode.length);
            //}

            //return data;
        }

        private byte[] finalSignAuthCode_MD5_128(byte[] encryptedPayLoad)
        {
            throw new NotImplementedException();
            //byte[] tempData = this.finalSignAuthCode_NONE(encryptedPayLoad);
            //int padSize = 4 - (tempData.length + 2) % 4;
            //byte[] data = new byte[tempData.length + padSize + 2 + 16];
            //System.arraycopy(tempData, 0, data, 0, tempData.length);

            //for (int i = 0; i < padSize; ++i)
            //{
            //    data[tempData.length + i] = -1;
            //}

            //data[tempData.length + padSize] = (byte)padSize;
            //data[tempData.length + padSize + 1] = 7;
            //byte[] temp = new byte[tempData.length + padSize + 2];
            //System.arraycopy(data, 0, temp, 0, temp.length);
            //byte[] temp2 = new byte[temp.length - 4];
            //System.arraycopy(temp, 4, temp2, 0, temp2.length);
            //byte[] paddingPassword = new byte[20];
            //System.arraycopy(this.config.getPassword().getBytes(), 0, paddingPassword, 0, this.config.getPassword().length());
            //byte[] tempAll = new byte[temp2.length + paddingPassword.length * 2];
            //System.arraycopy(paddingPassword, 0, tempAll, 0, paddingPassword.length);
            //System.arraycopy(temp2, 0, tempAll, paddingPassword.length, temp2.length);
            //System.arraycopy(paddingPassword, 0, tempAll, temp2.length + paddingPassword.length, paddingPassword.length);
            //byte[] authCode = (new MD5_128()).code(this.rmcpPlusSession.K1, tempAll);
            //if (authCode != null)
            //{
            //    System.arraycopy(authCode, 0, data, tempData.length + padSize + 2, authCode.length);
            //}

            //return data;
        }
        private async Task<bool> OpenSession()
        {
            SetCipherSuiteById(3);
            CreateRAKPOpenSessionMessage();

            var result = await sendRAKPMessage(16, openSessionRequest.GetBytes());
            if (result == null)
            {
                throw new Exception("Send RAKP Open Session Request error");
            }
            if (!ReadOpenSessionResponse(result))
            {
                throw new Exception("failed to read session open response");
            }

            createRAKPMessage1();

            result = await sendRAKPMessage(18, rakpMessage1.GetBytes());
            if (result == null)
            {
                throw new Exception("Send RAKP Open Session Request error");
            }
            if (!resolveRAKPMessage2(result))
            {
                throw new Exception("failed to read session rakp message 1");
            }

            createRAKPMessage3();
            result = await sendRAKPMessage(20, rakpMessage3.GetBytes());
            if (result == null)
            {
                throw new Exception("Send RAKP Message 3 error");
            }

            if (!resolveRAKPMessage4(result))
            {
                throw new Exception("Read RAKP Message 4 error");
            }
            this.sessionID = BitConverter.ToUInt32(openSessionResponse.managedSystemSessionID);
            IncreaseSessionSeq();
            return true;
        }

        private void SetCipherSuiteById(int id)
        {
            authenticationAlgorithm = CipherSuite.CIPHER_SUITES[id][0];
            integrityAlgorithm = CipherSuite.CIPHER_SUITES[id][1];
            confidentialityAlgorithm = CipherSuite.CIPHER_SUITES[id][2];
        }

        public static void intTo2Bytes(byte[] bytedest, int offset, int intsrc)
        {
            bytedest[offset + 0] = (byte)intsrc;
            bytedest[offset + 1] = (byte)(intsrc >> 8);
        }
        private bool ReadOpenSessionResponse(byte[] recvBuf)
        {
            int index = 16;
            byte payLoadType = recvBuf[5];
            if (payLoadType != 17)
            {
                throw new Exception("Open Session Response error: Not a Open Session Response Payload");
            }
            this.openSessionResponse.messageTag = recvBuf[index];
            this.openSessionResponse.RMCPPlusStatusCode = recvBuf[++index];
            if (this.openSessionResponse.RMCPPlusStatusCode != 0)
            {
                throw new Exception(RMCPPlusStatusCode[this.openSessionResponse.RMCPPlusStatusCode]);
            }
            this.openSessionResponse.maxPrivilegeLevel = recvBuf[++index];
            ++index;
            Array.Copy(recvBuf, ++index, this.openSessionResponse.remoteConsoleSessionID, 0, 4);
            Array.Copy(recvBuf, index += 4, this.openSessionResponse.managedSystemSessionID, 0, 4);
            Array.Copy(recvBuf, index += 4, this.openSessionResponse.authenticationPayload, 0, 8);
            Array.Copy(recvBuf, index += 8, this.openSessionResponse.integrityPayload, 0, 8);
            Array.Copy(recvBuf, index += 8, this.openSessionResponse.confidentialityPayload, 0, 8);
            Array.Copy(this.openSessionResponse.managedSystemSessionID, 0, BitConverter.GetBytes(sessionID), 0, 4);
            return true;
        }
        private void CreateRAKPOpenSessionMessage()
        {
            this.openSessionRequest.messageTag = this.messageTag;
            this.openSessionRequest.requestedMaxPrivilegeLevel = 4;//this.config.getPrivilege();
            this.openSessionRequest.reserved[0] = 0;
            this.openSessionRequest.reserved[1] = 0;
            consoleSessionIDCount = (byte)(consoleSessionIDCount + 1);
            Array.Copy(this.consoleSessionID, 0, this.openSessionRequest.remoteConsoleSessionID, 0, 4);
            byte[] auth = new byte[] { 0, 0, 0, 8, CipherSuite.getAuthenticationAlgorithmByID(cipherSuite), 0, 0, 0 };
            Array.Copy(auth, 0, this.openSessionRequest.authenticationPayload, 0, 8);
            byte[] integirty = new byte[] { 1, 0, 0, 8, CipherSuite.getIntegrityAlgorithmByID(cipherSuite), 0, 0, 0 };
            Array.Copy(integirty, 0, this.openSessionRequest.integrityPayload, 0, 8);
            byte[] confidentiality = new byte[] { 2, 0, 0, 8, CipherSuite.getConfidentialityAlgorithmByID(cipherSuite), 0, 0, 0 };
            Array.Copy(confidentiality, 0, this.openSessionRequest.confidentialityPayload, 0, 8);
        }
        private void createRAKPMessage1()
        {
            this.rakpMessage1.messageTag = this.messageTag;
            byte[] reserved1 = new byte[] { 0, 0, 0 };
            Array.Copy(reserved1, 0, this.rakpMessage1.reserved1, 0, 3);
            Array.Copy(this.openSessionResponse.managedSystemSessionID, 0, this.rakpMessage1.managedSystemSessionID, 0, 4);

            byte[] rng = new byte[16];
            new Random().NextBytes(rng);

            Array.Copy(rng, 0, this.rakpMessage1.remoteConsoleRandomNumber, 0, 16);
            this.rakpMessage1.requestedMaxPrivilegeLevel = 4;// this.config.getPrivilege();
            byte[] reserved2 = new byte[] { 0, 0 };
            Array.Copy(reserved2, 0, this.rakpMessage1.reserved2, 0, 2);
            if (Username.Length > 0)
            {
                this.rakpMessage1.userNameLength = (byte)this.Username.Length;
                this.rakpMessage1.userName = new byte[this.Username.Length];
                this.rakpMessage1.userName = Encoding.ASCII.GetBytes(this.Username);
            }
            else
            {
                this.rakpMessage1.userNameLength = 0;
            }
        }

        private async Task<byte[]> sendRAKPMessage(byte type, byte[] payLoad)
        {
            sessionHeader.setPayLoadType(type);
            byte[] data = new byte[this.header.Size() + this.sessionHeader.Size() + 2 + payLoad.Length];
            int index = 0;
            Array.Copy(this.header.GetBytes(), 0, data, index, this.header.Size());
            Array.Copy(this.sessionHeader.GetBytes(), 0, data, index += this.header.Size(), this.sessionHeader.Size());
            byte[] payLoadLength = new byte[2];
            intTo2Bytes(payLoadLength, 0, payLoad.Length);
            Array.Copy(payLoadLength, 0, data, index += this.sessionHeader.Size(), payLoadLength.Length);
            Array.Copy(payLoad, 0, data, index += payLoadLength.Length, payLoad.Length);

            if (await client.SendAsync(data) == 0)
            {
                return null;
            }

            return (await client.ReceiveAsync()).Buffer;
        }

        private byte getCipherSuiteID()
        {
            return 3;
        }
        private bool resolveRAKPMessage2(byte[] recvBuf)
        {
            int index = 16;
            byte payLoadType = recvBuf[5];
            if (payLoadType != 19)
            {
                throw new Exception("RAKP Message 2 error: Not a RAKP Message 2 Payload");
                return false;
            }
            this.rakpMessage2.messageTag = recvBuf[index];
            this.rakpMessage2.RMCPPlusStatusCode = recvBuf[++index];
            if (this.rakpMessage2.RMCPPlusStatusCode != 0)
            {
                throw new Exception(RMCPPlusStatusCode[this.rakpMessage2.RMCPPlusStatusCode]);
                return false;
            }
            ++index;
            ++index;
            Array.Copy(recvBuf, ++index, this.rakpMessage2.remoteConsoleSessionID, 0, 4);
            Array.Copy(recvBuf, index += 4, this.rakpMessage2.managedSystemRandomNumber, 0, 16);
            Array.Copy(recvBuf, index += 16, this.rakpMessage2.managedSystemGUID, 0, 16);
            index += 16;
            switch (CipherSuite.getAuthenticationAlgorithmByID(getCipherSuiteID()))
            {
                case 0:
                    {
                        break;
                    }
                case 2:
                    {
                        this.rakpMessage2.keyExchangeAuthenticationCode = new byte[16];
                        Array.Copy(recvBuf, index, this.rakpMessage2.keyExchangeAuthenticationCode, 0, 16);
                        break;
                    }
                case 1:
                    {
                        this.rakpMessage2.keyExchangeAuthenticationCode = new byte[20];
                        Array.Copy(recvBuf, index, this.rakpMessage2.keyExchangeAuthenticationCode, 0, 20);
                        break;
                    }
            }
            bool hmac = this.checkRAKPMessage2HMAC();
            if (!hmac)
            {
                throw new Exception("RAKP Message 2 error: Verify error. Maybe the password invalid");
                return false;
            }

            this.SIK = this.calculateSIK();
            this.K1 = this.calculateK1();
            this.K2 = this.calculateK2();
            return true;
        }

        private byte[] calculateSIK()
        {
            byte[] plainText = null;
            int size = 0;
            size = this.rakpMessage1.remoteConsoleRandomNumber.Length + this.rakpMessage2.managedSystemRandomNumber.Length + 1 + 1;
            if (this.rakpMessage1.userNameLength != 0)
            {
                size += this.rakpMessage1.userName.Length;
            }
            plainText = new byte[size];
            Array.Copy(this.rakpMessage1.remoteConsoleRandomNumber, 0, plainText, 0, 16);
            Array.Copy(this.rakpMessage2.managedSystemRandomNumber, 0, plainText, 16, 16);
            plainText[32] = this.rakpMessage1.requestedMaxPrivilegeLevel;
            plainText[33] = this.rakpMessage1.userNameLength;
            if (this.rakpMessage1.userNameLength != 0)
            {
                Array.Copy(this.rakpMessage1.userName, 0, plainText, 34, this.rakpMessage1.userName.Length);
            }
            byte[] result = CipherAlgorithmFactory.createRAKPAlgorithm(this.authenticationAlgorithm).mac(Encoding.ASCII.GetBytes(Password), plainText);
            return result;
        }

        private byte[] calculateK1()
        {
            if (CipherSuite.getIntegrityAlgorithmByID(cipherSuite) == 0)
            {
                return new byte[0];
            }
            byte[] const1 = new byte[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };
            byte[] k1 = CipherAlgorithmFactory.createRAKPAlgorithm(authenticationAlgorithm).mac(this.SIK, const1);
            byte[] k1_128 = new byte[16];
            Array.Copy(k1, 0, k1_128, 0, 16);
            if (CipherSuite.getIntegrityAlgorithmByID(cipherSuite) == 1)
            {
                return k1;
            }
            return k1_128;
        }

        private byte[] calculateK2()
        {
            if (CipherSuite.getIntegrityAlgorithmByID(cipherSuite) == 0)
            {
                return new byte[0];
            }
            byte[] const2 = new byte[] { 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 };
            byte[] k2 = CipherAlgorithmFactory.createRAKPAlgorithm(authenticationAlgorithm).mac(this.SIK, const2);
            byte[] k2_128 = new byte[16];
            Array.Copy(k2, 0, k2_128, 0, 16);
            return k2_128;
        }



        private void createRAKPMessage3()
        {
            this.rakpMessage3.messageTag = this.messageTag;
            this.rakpMessage3.RMCPPlusStatusCode = 0;
            byte[] reserved = new byte[] { 0, 0 };
            Array.Copy(reserved, 0, this.rakpMessage3.reserved, 0, 2);
            Array.Copy(this.openSessionResponse.managedSystemSessionID, 0, this.rakpMessage3.managedSystemSessionID, 0, 4);
            this.rakpMessage3.keyExchangeAuthenticationCode = this.createRAKPMessage3HMAC();
        }
        private byte[] createRAKPMessage3HMAC()
        {
            if (CipherSuite.getAuthenticationAlgorithmByID(cipherSuite) == 0)
            {
                return new byte[0];
            }
            byte[] plainText = null;
            int size = 0;
            size = this.rakpMessage2.managedSystemRandomNumber.Length + this.consoleSessionID.Length + 1 + 1;
            if (this.rakpMessage1.userNameLength != 0)
            {
                size += this.rakpMessage1.userName.Length;
            }
            plainText = new byte[size];
            Array.Copy(this.rakpMessage2.managedSystemRandomNumber, 0, plainText, 0, 16);
            Array.Copy(this.consoleSessionID, 0, plainText, 16, 4);
            plainText[20] = this.rakpMessage1.requestedMaxPrivilegeLevel;
            plainText[21] = this.rakpMessage1.userNameLength;
            if (this.rakpMessage1.userNameLength != 0)
            {
                Array.Copy(this.rakpMessage1.userName, 0, plainText, 22, this.rakpMessage1.userName.Length);
            }
            byte[] result = GlobalDefine.OEM_RAKP_FLAG || false ? CipherAlgorithmFactory.createRAKPAlgorithm(authenticationAlgorithm).mac(KG, plainText) : CipherAlgorithmFactory.createRAKPAlgorithm(authenticationAlgorithm).mac(Encoding.ASCII.GetBytes(Password), plainText);
            return result;
        }
        byte cipherSuite = 3;
        private bool checkRAKPMessage2HMAC()
        {
            byte[] result;
            if (CipherSuite.getAuthenticationAlgorithmByID(getCipherSuiteID()) == 0)
            {
                return true;
            }
            byte[] plainText = null;
            int size = this.consoleSessionID.Length + this.openSessionResponse.managedSystemSessionID.Length + this.rakpMessage1.remoteConsoleRandomNumber.Length + this.rakpMessage2.managedSystemRandomNumber.Length + this.rakpMessage2.managedSystemGUID.Length + 1 + 1;
            if (this.rakpMessage1.userNameLength != 0)
            {
                size += this.rakpMessage1.userName.Length;
            }
            plainText = new byte[size];
            Array.Copy(this.consoleSessionID, 0, plainText, 0, 4);
            Array.Copy(this.openSessionResponse.managedSystemSessionID, 0, plainText, 4, 4);
            Array.Copy(this.rakpMessage1.remoteConsoleRandomNumber, 0, plainText, 8, 16);
            Array.Copy(this.rakpMessage2.managedSystemRandomNumber, 0, plainText, 24, 16);
            Array.Copy(this.rakpMessage2.managedSystemGUID, 0, plainText, 40, 16);
            plainText[56] = this.rakpMessage1.requestedMaxPrivilegeLevel;
            plainText[57] = this.rakpMessage1.userNameLength;
            if (this.rakpMessage1.userNameLength != 0)
            {
                Array.Copy(this.rakpMessage1.userName, 0, plainText, 58, this.rakpMessage1.userName.Length);
            }
            return bytesToHex(result = GlobalDefine.OEM_RAKP_FLAG || false ? CipherAlgorithmFactory.createRAKPAlgorithm(authenticationAlgorithm).mac(KG, plainText) : CipherAlgorithmFactory.createRAKPAlgorithm(authenticationAlgorithm).mac(Encoding.ASCII.GetBytes(Password), plainText)).Equals(bytesToHex(this.rakpMessage2.keyExchangeAuthenticationCode));
        }
        private bool resolveRAKPMessage4(byte[] recvBuf)
        {
            int index = 16;
            byte payLoadType = recvBuf[5];
            if (payLoadType != 21)
            {
                throw new Exception("RAKP Message 4 error: Not a RAKP Message 4 Payload");
                return false;
            }
            this.rakpMessage4.messageTag = recvBuf[index];
            this.rakpMessage4.RMCPPlusStatusCode = recvBuf[++index];
            if (this.rakpMessage4.RMCPPlusStatusCode != 0)
            {
                throw new Exception("RAKP Message 4 error: " + RMCPPlusStatusCode[this.rakpMessage4.RMCPPlusStatusCode]);
                return false;
            }
            ++index;
            ++index;
            Array.Copy(recvBuf, ++index, this.rakpMessage4.mgmtConsoleSessionID, 0, 4);
            index += 4;
            switch (CipherSuite.getAuthenticationAlgorithmByID(getCipherSuiteID()))
            {
                case 0:
                    {
                        break;
                    }
                case 2:
                    {
                        this.rakpMessage4.integrityCheckValue = new byte[16];
                        Array.Copy(recvBuf, index, this.rakpMessage4.integrityCheckValue, 0, 16);
                        break;
                    }
                case 1:
                    {
                        this.rakpMessage4.integrityCheckValue = new byte[12];
                        Array.Copy(recvBuf, index, this.rakpMessage4.integrityCheckValue, 0, 12);
                        break;
                    }
            }

            if (!checkRAKPMessage4HMAC())
            {
                throw new Exception("RAKP Message 4 error: Verify error. Maybe the password invalid");
                return false;
            }
            Console.WriteLine(" RAKP 4 Key verify Successful");
            return true;
        }
        private bool checkRAKPMessage4HMAC()
        {
            if (authenticationAlgorithm == 0)
            {
                return true;
            }
            byte[] plainText = null;
            int size = 0;
            size = this.rakpMessage1.remoteConsoleRandomNumber.Length + this.openSessionResponse.managedSystemSessionID.Length + this.rakpMessage2.managedSystemGUID.Length;
            plainText = new byte[size];
            Array.Copy(this.rakpMessage1.remoteConsoleRandomNumber, 0, plainText, 0, 16);
            Array.Copy(this.openSessionResponse.managedSystemSessionID, 0, plainText, 16, 4);
            Array.Copy(this.rakpMessage2.managedSystemGUID, 0, plainText, 20, 16);
            byte[] result = CipherAlgorithmFactory.createRAKPAlgorithm(authenticationAlgorithm).mac(this.SIK, plainText);

            if (this.authenticationAlgorithm == 1)
            {
                byte[] b1 = new byte[12];
                byte[] b2 = new byte[12];
                Array.Copy(result, 0, b1, 0, 12);
                Array.Copy(this.rakpMessage4.integrityCheckValue, 0, b2, 0, 12);
                if (!bytesToHex(b1).Equals(bytesToHex(b2)))
                {
                    Console.WriteLine("RAKP4 Integrity check value error");
                    //this.xMessage.setText(L.t("rakp.RAKP4_Integrity_check_value_error"));
                    //Logger.writeLog("RAKP4 Integrity check value error");
                    return false;
                }
            }
            else if (!bytesToHex(result).Equals(bytesToHex(this.rakpMessage4.integrityCheckValue)))
            {
                Console.WriteLine("RAKP4 Integrity check value error");
                // Logger.writeLog(");
                // this.xMessage.setText(L.t("rakp.RAKP4_Integrity_check_value_error"));
                return false;
            }
            return true;
        }

        public static char[] hex = "0123456789ABCDEF".ToCharArray();
        public static String bytesToHex(byte[] b2)
        {
            if (b2 == null)
            {
                return "";
            }
            char[] ret = new char[b2.Length * 3];
            int j2 = 0;
            for (int i2 = 0; i2 < b2.Length; ++i2)
            {
                ret[j2++] = hex[(b2[i2] & 0xF0) >> 4];
                ret[j2++] = hex[b2[i2] & 0xF];
                ret[j2++] = (char)32;
            }
            return new String(ret);
        }
        public static bool isP8DTUGUID(byte[] guid)
        {
            if (guid == null || guid.Length == 0)
            {
                return false;
            }
            return guid[0] == 53 && guid[1] == 49 && guid[2] == 48 && guid[3] == 49 && guid[4] == 77 && guid[5] == 83;
        }
        public static bool isPOWER9GUID(byte[] guid)
        {
            if (guid == null || guid.Length == 0)
            {
                return false;
            }
            return guid[0] == 55 && guid[1] == 49 && guid[2] == 48 && guid[3] == 49 && guid[4] == 77 && guid[5] == 83;
        }
        public async Task<IPMIMessage> GetProductID_P8()
        {
            IPMIMessage ipmiMessage = new IPMIMessage();
            byte[] data = new byte[] { 1 };
            ipmiMessage.setCommandAndData(192, 33, data);
            return await SendIpmiMessage(ipmiMessage);
        }
        public async Task<IPMIMessage> GetProductID()
        {
            IPMIMessage ipmiMessage = new IPMIMessage();
            byte[] data = new byte[] { };
            ipmiMessage.setCommandAndData(192, 33, data);
            return await SendIpmiMessage(ipmiMessage);
        }
        private async Task<byte[]> GetSystemGUID()
        {
            IPMIMessage ipmiMessage = new IPMIMessage();

            // Get System GUI
            byte[]? data = null;
            ipmiMessage.setCommandAndData(24, 55, data);
            IPMIMessage resMessage = await SendIpmiMessage(ipmiMessage);
            return resMessage.data;
        }
        public async Task<string> GetBoardInfo()
        {
            IPMIMessage ipmiMessage = new IPMIMessage();

            var guid = await GetSystemGUID();
            IPMIMessage resMessage = isP8DTUGUID(guid) || isPOWER9GUID(guid) ? await GetProductID_P8() : await GetProductID();

            // Get System GUI
            byte[]? data = null;
            ipmiMessage.setCommandAndData(24, 55, data);

            if (resMessage.completionCode == 0)
            {
                byte[] product = resMessage.data;
                foreach (var board in IpmiBoardInfo.boards)
                {
                    var b1 = int.Parse(board[1], System.Globalization.NumberStyles.HexNumber);
                    var b2 = int.Parse(board[2], System.Globalization.NumberStyles.HexNumber);
                    if ((b1 == product[1] && b2 == product[0]))
                    // (b1 == product[0] && b2 == product[1]))
                    {
                        return board[0];
                    }
                }

                return "unknown";
            }

            return "IPMI cmd fail";
        }
    }

    public class CipherSuite
    {
        public static byte[][] CIPHER_SUITES = new byte[][] {
            // authentication, integrity, confidentalitlity
            new byte[] { 0, 0, 0 },
            new byte[] { 1, 0, 0 },
            new byte[] { 1, 1, 0 },
            new byte[] { 1, 1, 1 },
            new byte[] { 1, 1, 2 },
            new byte[] { 1, 1, 3 },
            new byte[] { 2, 0, 0 },
            new byte[] { 2, 2, 0 },
            new byte[] { 2, 2, 1 },
            new byte[] { 2, 2, 2 },
            new byte[] { 2, 2, 3 },
            new byte[] { 2, 3, 0 },
            new byte[] { 2, 3, 1 },
            new byte[] { 2, 3, 2 },
            new byte[] { 2, 3, 3 }
        };
        public static byte getAuthenticationAlgorithmByID(byte id)
        {
            return CIPHER_SUITES[id][0];
        }
        public static byte getIntegrityAlgorithmByID(byte id)
        {
            return CIPHER_SUITES[id][1];
        }

        public static byte getConfidentialityAlgorithmByID(byte id)
        {
            return CIPHER_SUITES[id][2];
        }
    }

    public class SessionOpenRequest
    {
        public byte messageTag;
        public byte requestedMaxPrivilegeLevel;
        public byte[] reserved = new byte[2];
        public byte[] remoteConsoleSessionID = new byte[4];
        public byte[] authenticationPayload = new byte[8];
        public byte[] integrityPayload = new byte[8];
        public byte[] confidentialityPayload = new byte[8];

        public byte[] GetBytes()
        {
            byte[] raw = new byte[32];
            raw[0] = this.messageTag;
            raw[1] = this.requestedMaxPrivilegeLevel;
            Array.Copy(this.reserved, 0, raw, 2, 2);
            Array.Copy(this.remoteConsoleSessionID, 0, raw, 4, 4);
            Array.Copy(this.authenticationPayload, 0, raw, 8, 8);
            Array.Copy(this.integrityPayload, 0, raw, 16, 8);
            Array.Copy(this.confidentialityPayload, 0, raw, 24, 8);
            return raw;
        }
    }
    public class SessionHeader
    {
        public byte AuthType = (byte)6;
        public byte payLoadType = 0;
        public uint SessionID = 0;
        public uint SessionSeq = 0;
        public byte[] OEM_IANA = new byte[] { 0, 0, 0, 0 };
        public byte[] OEM_Payload_ID = new byte[] { 0, 0 };

        public byte Size()
        {
            return 10;
        }

        public byte[] GetBytes()
        {
            byte[] raw = new byte[this.Size()];
            raw[0] = this.AuthType;
            raw[1] = this.payLoadType;
            Array.Copy(BitConverter.GetBytes(SessionID), 0, raw, 2, 4);
            Array.Copy(BitConverter.GetBytes(SessionSeq), 0, raw, 6, 4);
            return raw;
        }

        public void setPayLoadType(byte payLoadType)
        {
            this.payLoadType = payLoadType;
        }
    }

    public class RMCPHeader
    {
        public static byte IPMI_Message = 7;
        public static byte ASF_Message = 6;
        byte version = (byte)6;
        byte reserved = 0;
        sbyte RMCPSeqNum = -1;
        byte classOfMesg = (byte)7;

        public byte[] GetBytes()
        {
            byte[] raw = new byte[] { this.version, this.reserved, (byte)this.RMCPSeqNum, this.classOfMesg };
            return raw;
        }

        public byte Size()
        {
            return 4;
        }
    }
    public class OpenSessionResponse
    {
        public byte messageTag;
        public byte RMCPPlusStatusCode;
        public byte maxPrivilegeLevel;
        public byte[] remoteConsoleSessionID = new byte[4];
        public byte[] managedSystemSessionID = new byte[4];
        public byte[] authenticationPayload = new byte[8];
        public byte[] integrityPayload = new byte[8];
        public byte[] confidentialityPayload = new byte[8];
    }

    public class RAKPMessage2
    {
        public byte messageTag;
        public byte RMCPPlusStatusCode;
        public byte[] remoteConsoleSessionID = new byte[4];
        public byte[] managedSystemRandomNumber = new byte[16];
        public byte[] managedSystemGUID = new byte[16];
        public byte[] keyExchangeAuthenticationCode = null;
    }

    public class RAKPMessage1
    {
        public byte messageTag;
        public byte[] reserved1 = new byte[3];
        public byte[] managedSystemSessionID = new byte[4];
        public byte[] remoteConsoleRandomNumber = new byte[16];
        public byte requestedMaxPrivilegeLevel;
        public byte[] reserved2 = new byte[2];
        public byte userNameLength;
        public byte[] userName;

        public byte[] GetBytes()
        {
            byte[] raw = new byte[28 + this.userNameLength];
            raw[0] = this.messageTag;
            Array.Copy(this.reserved1, 0, raw, 1, 3);
            Array.Copy(this.managedSystemSessionID, 0, raw, 4, 4);
            Array.Copy(this.remoteConsoleRandomNumber, 0, raw, 8, 16);
            raw[24] = this.requestedMaxPrivilegeLevel;
            Array.Copy(this.reserved2, 0, raw, 25, 2);
            raw[27] = this.userNameLength;
            if (this.userNameLength != 0 && this.userName.Length != 0)
            {
                Array.Copy(this.userName, 0, raw, 28, this.userName.Length);
            }
            return raw;
        }
    }

    public class RAKPMessage3
    {
        public byte messageTag;
        public byte RMCPPlusStatusCode;
        public byte[] reserved = new byte[2];
        public byte[] managedSystemSessionID = new byte[4];
        public byte[] keyExchangeAuthenticationCode = null;

        public byte[] GetBytes()
        {
            byte[] raw = new byte[8 + this.getAuthenticationByteSize()];
            raw[0] = this.messageTag;
            raw[1] = this.RMCPPlusStatusCode;
            Array.Copy(this.reserved, 0, raw, 2, 2);
            Array.Copy(this.managedSystemSessionID, 0, raw, 4, 4);
            switch (CipherSuite.getAuthenticationAlgorithmByID(3)) // misha replaced
            {
                case 0:
                    {
                        break;
                    }
                case 2:
                    {
                        Array.Copy(this.keyExchangeAuthenticationCode, 0, raw, 8, 16);
                        break;
                    }
                case 1:
                    {
                        Array.Copy(this.keyExchangeAuthenticationCode, 0, raw, 8, 20);
                        break;
                    }
            }
            return raw;
        }

        private byte getAuthenticationByteSize()
        {
            byte b2 = 0;
            switch (CipherSuite.getAuthenticationAlgorithmByID(3)) // misha:replaced
            {
                case 0:
                    {
                        b2 = 0;
                        break;
                    }
                case 2:
                    {
                        b2 = 16;
                        break;
                    }
                case 1:
                    {
                        b2 = 20;
                        break;
                    }
            }
            return b2;
        }
    }

    public class RAKPMessage4
    {
        public byte messageTag;
        public byte RMCPPlusStatusCode;
        public byte[] mgmtConsoleSessionID = new byte[4];
        public byte[] integrityCheckValue;

    }
    public class IPMIMessage
    {
        public const byte REQUEST = 0;
        public const byte RESPONSE = 1;
        public byte rsSA = 32;
        public byte netFnLun;
        public byte checkSum1;
        public byte rqSA = 65;
        public byte rqSeqLun;
        public byte cmd;
        public byte[]? data;
        public byte checkSum2;
        public byte completionCode = 0;
        public byte direction = 0;

        public void setRequest()
        {
            this.direction = 0;
        }

        public void setResponse()
        {
            this.direction = 1;
        }

        public void setCommandAndData(byte netFn, byte cmd, byte[]? bytes)
        {
            this.setCommand(netFn, cmd);
            this.setData(bytes);
        }

        public void setCommandAndData(byte netFn, byte cmd, byte rqSa, byte[] bytes)
        {
            this.setCommand(netFn, cmd, rqSa);
            this.setData(bytes);
        }

        public void setCommand(byte netFn, byte cmd, byte rqSa)
        {
            this.netFnLun = netFn;
            this.checkSum1 = this.calcCheckSum1();
            this.rqSA = rqSa;
            this.rqSeqLun = 0;
            this.cmd = cmd;
        }

        public void setCommand(byte netFn, byte cmd)
        {
            this.netFnLun = netFn;
            this.checkSum1 = this.calcCheckSum1();
            this.rqSeqLun = 0;
            this.cmd = cmd;
        }

        public void setData(byte[] bytes)
        {
            if (bytes == null)
            {
                this.data = new byte[0];
            }
            else
            {
                this.data = bytes;
            }

            this.checkSum2 = this.calcCheckSum2();
        }

        public byte calcCheckSum1()
        {
            byte value = 0;
            if (this.direction == 0)
            {
                value = (byte)(256 - (this.rsSA + this.netFnLun));
            }
            else if (this.direction == 1)
            {
                value = (byte)(256 - (this.rqSA + this.netFnLun));
            }

            return value;
        }

        public byte calcCheckSum2()
        {
            byte csum = 0;
            if (this.direction == 0)
            {
                csum = (byte)(this.rqSA + this.rqSeqLun + this.cmd);
            }
            else if (this.direction == 1)
            {
                csum = (byte)(this.rsSA + this.rqSeqLun + this.cmd + this.completionCode);
            }

            for (int i = 0; i < this.data.Length; ++i)
            {
                csum += this.data[i];
            }

            return (byte)(-csum);
        }

        public int size()
        {
            int size = 0;
            if (this.direction == 0)
            {
                size = 7 + this.data.Length;
            }
            else if (this.direction == 1)
            {
                size = 8 + this.data.Length;
            }

            return size;
        }

        public byte[] raw()
        {
            byte[] bytes = new byte[this.size()];
            if (this.direction == 0)
            {
                bytes[0] = this.rsSA;
                bytes[1] = this.netFnLun;
                bytes[2] = this.checkSum1;
                bytes[3] = this.rqSA;
                bytes[4] = this.rqSeqLun;
                bytes[5] = this.cmd;
                Array.Copy(this.data, 0, bytes, 6, this.data.Length);
                bytes[this.size() - 1] = this.checkSum2;
            }
            else if (this.direction == 1)
            {
                bytes[0] = this.rqSA;
                bytes[1] = this.netFnLun;
                bytes[2] = this.checkSum1;
                bytes[3] = this.rsSA;
                bytes[4] = this.rqSeqLun;
                bytes[5] = this.cmd;
                bytes[6] = this.completionCode;
                Array.Copy(this.data, 0, bytes, 7, this.data.Length);
                bytes[this.size() - 1] = this.checkSum2;
            }

            return bytes;
        }

        public byte[] humanReadRaw()
        {
            byte[] raw = new byte[0];
            if (this.direction == 0)
            {
                raw = new byte[2 + this.data.Length];
                raw[0] = (byte)((this.netFnLun & 255) >> 2);
                raw[1] = this.cmd;
                Array.Copy(this.data, 0, raw, 2, this.data.Length);
            }
            else if (this.direction == 1)
            {
                raw = new byte[1 + this.data.Length];
                raw[0] = this.completionCode;
                Array.Copy(this.data, 0, raw, 1, this.data.Length);
            }

            return raw;
        }

        public static IPMIMessage fromRaw(byte[] bytes)
        {
            IPMIMessage msg = new IPMIMessage();
            msg.direction = 1;
            msg.rqSA = bytes[0];
            msg.netFnLun = bytes[1];
            msg.checkSum1 = bytes[2];
            msg.rsSA = bytes[3];
            msg.rqSeqLun = bytes[4];
            msg.cmd = bytes[5];
            msg.completionCode = bytes[6];
            msg.data = new byte[bytes.Length >= 8 ? bytes.Length - 8 : 0];
            Array.Copy(bytes, 7, msg.data, 0, msg.data.Length);
            msg.checkSum2 = bytes[bytes.Length - 1];
            return msg;
        }
    }
}
