using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using ENet;
using Server;
using Random = UnityEngine.Random;
using UniGLTF;
using UniGLTF.Zip;
using Crosstales.FB;

[RequireComponent(typeof(AudioSource))]
public class SceneManager : MonoBehaviour {

    public GameObject myPlayerFactory;
    public GameObject otherPlayerFactory;
    public GameObject ballFactory;
    public Material ballMaterial;

    private GameObject _myPlayer;
    private GameObject _ball;
    private uint _myPlayerId;
    private uint _ballId;

    private Host _client;
    private Peer _peer;
    private int _skipFrame = 0;
    private Dictionary<uint, GameObject> _players = new Dictionary<uint, GameObject>();

    const int channelID = 0;

    protected bool myControl { get; set; }

    [SerializeField]
    private AudioSource source;
    public AudioClip myClip;
    public AudioSource outputAudio;

    public KeyCode pushToTalkKey;

    public enum MicrophoneQuality { VERYLOW, HIGH, VERYHIGH }
    private bool isPlaying;

    [SerializeField]
    Button m_importButton;
    [SerializeField]
    Button m_uploadButton;

    GameObject m_root;

    [SerializeField]
    InputField m_redInput;
    [SerializeField]
    InputField m_greenInput;
    [SerializeField]
    InputField m_blueInput;

    private string path = "";

    void Start ()
    {
        source.clip = myClip;
        this.isPlaying = false;

        Application.runInBackground = true;
        InitENet();
        _myPlayer = Instantiate(myPlayerFactory);
        _ball = Instantiate(ballFactory);

        source = GetComponent<AudioSource>();
        outputAudio = GetComponent<AudioSource>();

        m_importButton.onClick.AddListener(OnClickImport);
        m_uploadButton.onClick.AddListener(OnClickUpload);
    }

	void Update ()
    {
        catchRequest();
    }

    public void OnClicked(Button button)
    {
        print(button.name);
    }

    public void catchRequest() {
        if (Input.GetKeyDown("space"))
        {
            Debug.Log("catch request");
            var protocol = new Protocol();
            var buffer = protocol.Serialize((byte)PacketId.ChangeStatus, _myPlayerId);
            var packet = default(Packet);
            packet.Create(buffer);
            _peer.Send(channelID, ref packet);
        }
        if (Input.GetKeyDown("left ctrl"))
        {
            float red = float.Parse(m_redInput.text);
            float green = float.Parse(m_greenInput.text);
            float blue = float.Parse(m_blueInput.text);

            Debug.Log("change color request");
            ballMaterial.color = new Color(red, green, blue, 1);

            print($"color: {ballMaterial.color.r}, {ballMaterial.color.g}, {ballMaterial.color.b}");

            var protocol = new Protocol();
            var buffer = protocol.Serialize((byte)PacketId.ChangeColorRequest, _myPlayerId, red, green, blue);
            var packet = default(Packet);
            packet.Create(buffer);
            _peer.Send(channelID, ref packet);
        }

    }

    void FixedUpdate()
    {
        UpdateENet();

        if (++_skipFrame < 3)
            return;

        SendPositionUpdate();
        _skipFrame = 0;

        sendAudio();
    }

    void OnDestroy()
    {
        _client.Dispose();
        ENet.Library.Deinitialize();
    }

    private void InitENet()
    {
        const string ip = "172.30.26.140";
        const ushort port = 1234;
        ENet.Library.Initialize();
        _client = new Host();
        Address address = new Address();

        address.SetHost(ip);
        address.Port = port;
        _client.Create();
        Debug.Log("Connecting");
        _peer = _client.Connect(address);
    }

    private void UpdateENet()
    {
        ENet.Event netEvent;

        if (_client.CheckEvents(out netEvent) <= 0)
        {
            if (_client.Service(15, out netEvent) <= 0)
                return;
        }

        switch (netEvent.Type)
        {
            case ENet.EventType.None:
                break;

            case ENet.EventType.Connect:
                //Debug.Log("Client connected to server - ID: " + _peer.ID);
                SendLogin();
                break;

            case ENet.EventType.Disconnect:
                //Debug.Log("Client disconnected from server");
                break;

            case ENet.EventType.Timeout:
                //Debug.Log("Client connection timeout");
                break;

            case ENet.EventType.Receive:
                //Debug.Log("Packet received from server - Channel ID: " + netEvent.ChannelID + ", Data length: " + netEvent.Packet.Length);
                ParsePacket(ref netEvent);
                netEvent.Packet.Dispose();
                break;
        }
    }

    enum PacketId : byte
    {
        LoginRequest = 1,
        LoginResponse = 2,
        LoginEvent = 3,
        PositionUpdateRequest = 4,
        PositionUpdateEvent = 5,
        LogoutEvent = 6,
        ChangeStatus = 7,
        ChangeStatusEvent = 8,
        ChangeColorRequest = 9,
        ChangeColorEvent = 10,
        resetRequest = 11,
        AudioRequest = 12,
        AudioEvent = 13,
        UploadObjRequest = 14,
        UploadObjEvent = 15,
        PlayerPositionUpdateRequest = 16,
        PlayerPositionUpdateEvent = 17,
    }

    private void SendPositionUpdate()
    {
        if (myControl)
        {
            var x = _ball.GetComponent<Rigidbody>().position.x;
            var y = _ball.GetComponent<Rigidbody>().position.y;
            var z = _ball.GetComponent<Rigidbody>().position.z;

            var protocol = new Protocol();
            var buffer = protocol.Serialize((byte)PacketId.PositionUpdateRequest, _myPlayerId, x, y, z);
            var packet = default(Packet);
            packet.Create(buffer);
            _peer.Send(channelID, ref packet);
        }

        var xp = _myPlayer.GetComponent<Rigidbody>().position.x;
        var yp = _myPlayer.GetComponent<Rigidbody>().position.y;
        var zp = _myPlayer.GetComponent<Rigidbody>().position.z;
        var xq = _myPlayer.GetComponent<Rigidbody>().rotation.x;
        var yq = _myPlayer.GetComponent<Rigidbody>().rotation.y;
        var zq = _myPlayer.GetComponent<Rigidbody>().rotation.z;
        var wq = _myPlayer.GetComponent<Rigidbody>().rotation.w;

        var protocolp = new Protocol();
        var bufferp = protocolp.Serialize((byte)PacketId.PlayerPositionUpdateRequest, _myPlayerId, xp, yp, zp, xq, yq, zq, wq);
        var packetp = default(Packet);
        packetp.Create(bufferp);
        _peer.Send(channelID, ref packetp);
    }

    private void SendLogin()
    {
        Debug.Log("SendLogin");
        var protocol = new Protocol();
        var buffer = protocol.Serialize((byte)PacketId.LoginRequest, 0);
        var packet = default(Packet);
        packet.Create(buffer);
        _peer.Send(channelID, ref packet);
    }

    public void sendAudio()
    {
        if (Input.GetKeyDown(pushToTalkKey))
        {
            Debug.Log("Send Audio");
            UseMic(true, MicrophoneQuality.VERYHIGH);
        }
        if (Input.GetKeyUp(pushToTalkKey))
        {
            print("End Audio");
            UseMic(false, MicrophoneQuality.HIGH);
        }

    }

    private void ParsePacket(ref ENet.Event netEvent)
    {
        var readBuffer = new byte[8000000];
        var readStream = new MemoryStream(readBuffer);
        var reader = new BinaryReader(readStream);

        readStream.Position = 0;
        netEvent.Packet.CopyTo(readBuffer);
        var packetId = (PacketId)reader.ReadByte();

        //Debug.Log("ParsePacket received: " + packetId);

        if (packetId == PacketId.LoginResponse)
        {
            _myPlayerId = reader.ReadUInt32();
            Debug.Log("MyPlayerId: " + _myPlayerId);
        }
        else if (packetId == PacketId.LoginEvent)
        {
            var playerId = reader.ReadUInt32();
            Debug.Log("OtherPlayerId: " + playerId);
            SpawnOtherPlayer(playerId);
        }
        else if (packetId == PacketId.PositionUpdateEvent)
        {
            var playerId = reader.ReadUInt32();
            var x = reader.ReadSingle();
            var y = reader.ReadSingle();
            var z = reader.ReadSingle();

            //print($"ID: {playerId}, Pos: {x}, {y}, {z}");

            UpdatePosition(playerId, x, y, z);
        }
        else if (packetId == PacketId.PlayerPositionUpdateEvent)
        {
            var playerId = reader.ReadUInt32();
            var x = reader.ReadSingle();
            var y = reader.ReadSingle();
            var z = reader.ReadSingle();
            // Quaternion
            var xq = reader.ReadSingle();
            var yq = reader.ReadSingle();
            var zq = reader.ReadSingle();
            var wq = reader.ReadSingle();

            //print($"ID: {playerId}, Pos: {x}, {y}, {z}");

            UpdatePlayerPosition(playerId, x, y, z, xq, yq, zq, wq);
        }
        else if (packetId == PacketId.LogoutEvent)
        {
            var playerId = reader.ReadUInt32();
            if (_players.ContainsKey(playerId))
            {
                Destroy(_players[playerId]);
                _players.Remove(playerId);
            }
        }
        else if (packetId == PacketId.ChangeStatusEvent) {
            var playerId = reader.ReadUInt32();
            if(playerId != _myPlayerId)
                myControl = false;
            else
                myControl = true;
        }
        else if (packetId == PacketId.ChangeColorEvent)
        {
            var playerId = reader.ReadUInt32();
            float red = (float)reader.ReadSingle();
            float green = (float)reader.ReadSingle();
            float blue = (float)reader.ReadSingle();
            print($"color: {red}, {green}, {blue}");
            ballMaterial.color = new Color(red, green, blue, 1);
        }
        else if (packetId == PacketId.AudioEvent)
        {
            var playerId = reader.ReadUInt32();
            var size = reader.ReadInt32();
            byte[] audio = reader.ReadBytes(size);

            print("Audio Received here");

            if (playerId == _myPlayerId)
                return;

            var samples = new float[audio.Length / 4];
            Buffer.BlockCopy(audio, 0, samples, 0, audio.Length);

            for (int i = 0; i < samples.Length; ++i)
            {
                samples[i] = samples[i] * 0.5f;
            }

            print("Audio PlayBack");
            print(samples.Length);
            source.clip.SetData(samples, 0);
            source.Play();

        }
        else if (packetId == PacketId.UploadObjEvent)
        {
            var playerId = reader.ReadUInt32();
            var size = reader.ReadInt32();
            byte[] obj = reader.ReadBytes(size);

            if (playerId == _myPlayerId)
                return;

            print("Obj Received here");
            m_root = Load(obj);

        }
    }

    private void SpawnOtherPlayer(uint playerId)
    {
        if (playerId == _myPlayerId)
            return;
        var newPlayer = Instantiate(otherPlayerFactory);
        newPlayer.transform.position = newPlayer.GetComponent<Rigidbody>().position;
        Debug.Log("Spawn other object " + playerId);
        _players[playerId] = newPlayer;
    }

    private void UpdatePosition(uint playerId, float x, float y, float z)
    {
        if (playerId == _myPlayerId)
            return;

        if(!myControl) _ball.transform.position = new Vector3(x, y, z);
    }

    private void UpdatePlayerPosition(uint playerId, float x, float y, float z, float xq, float yq, float zq, float wq)
    {
        if (playerId == _myPlayerId)
            return;

        //Debug.Log("UpdatePosition " + playerId);
        _players[playerId].transform.SetPositionAndRotation(new Vector3(x, y, z), new Quaternion(xq, yq, zq, wq));
    }

    public void UseMic(bool useMic, MicrophoneQuality qual)
    {
        int samplingRate = 44100;
        if (qual == MicrophoneQuality.VERYLOW){ samplingRate = 8000; }
        else if (qual == MicrophoneQuality.HIGH){ samplingRate = 44100; }
        else if (qual == MicrophoneQuality.VERYHIGH) { samplingRate = 48000; }
        if (useMic)
        {
            this.isPlaying = true;
            source.clip = Microphone.Start(Microphone.devices[0].ToString(), true, 1, samplingRate);
            //source.loop = true;

            while (!(Microphone.GetPosition(null) > 0)) {
                source.Play();
                float[] samples = new float[source.clip.samples * source.clip.channels];
                source.clip.GetData(samples, 0);

                for (int i = 0; i < samples.Length; ++i)
                {
                    samples[i] = samples[i] * 0.5f;
                }
                var byteArray = new byte[samples.Length * 4];
                Buffer.BlockCopy(samples, 0, byteArray, 0, byteArray.Length);

                //print(byteArray.Length);

                var protocol = new Protocol();
                var buffer = protocol.Serialize((byte)PacketId.AudioRequest, _myPlayerId, byteArray.Length, byteArray);
                var packet = default(Packet);
                packet.Create(buffer);
                _peer.Send(channelID, ref packet);

            }

        }
        else
        {
            this.isPlaying = false;
            source.Stop();  
            source.clip = null;
        }
    }

    public void OnClickImport()
    {
        print("Load File");
        //ExtensionFilter[] ext = new ExtensionFilter[3];
        //ext[0] = new ExtensionFilter("glb");
        //ext[1] = new ExtensionFilter("gltf");
        //ext[2] = new ExtensionFilter("zip");
        //path = FileBrowser.OpenSingleFile("Load gltf", Application.dataPath, ext);
        path = UnityEditor.EditorUtility.OpenFilePanel("open gltf", "", "gltf,glb");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (m_root != null)
        {
            GameObject.Destroy(m_root);
        }

        m_root = Load(path);
    }

    GameObject Load(string path)
    {
        var bytes = File.ReadAllBytes(path);

        Debug.LogFormat("[OnClick] {0}", path);
        var context = new ImporterContext();

        var ext = Path.GetExtension(path).ToLower();
        switch (ext)
        {
            case ".gltf":
                context.ParseJson(Encoding.UTF8.GetString(bytes), new FileSystemStorage(Path.GetDirectoryName(path)));
                break;

            case ".zip":
                {
                    var zipArchive = UniGLTF.Zip.ZipArchiveStorage.Parse(bytes);
                    var gltf = zipArchive.Entries.FirstOrDefault(x => x.FileName.ToLower().EndsWith(".gltf"));
                    if (gltf == null)
                    {
                        throw new Exception("no gltf in archive");
                    }
                    var jsonBytes = zipArchive.Extract(gltf);
                    var json = Encoding.UTF8.GetString(jsonBytes);
                    context.ParseJson(json, zipArchive);
                }
                break;

            case ".glb":
                context.ParseGlb(bytes);
                break;

            default:
                throw new NotImplementedException();
        }

        gltfImporter.Load(context);
        context.Root.name = Path.GetFileNameWithoutExtension(path);
        context.ShowMeshes();

        return context.Root;
    }

    GameObject Load(byte[] bytes)
    {
        Debug.LogFormat("[OnClick] {0}", path);
        var context = new ImporterContext();

        context.ParseGlb(bytes);

        gltfImporter.Load(context);
        context.Root.name = Path.GetFileNameWithoutExtension(path);
        context.ShowMeshes();

        return context.Root;
    }

    public void OnClickUpload()
    {
        print("Upload File");
        var bytes = File.ReadAllBytes(path);
        int size = bytes.Length;

        var protocol = new Protocol();
        var buffer = protocol.Serialize((byte)PacketId.UploadObjRequest, _myPlayerId, size, bytes);
        var packet = default(Packet);
        packet.Create(buffer);
        _peer.Send(channelID, ref packet);
    }

}
