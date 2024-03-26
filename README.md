# gnet_csharp
c# connector library for [gnet](https://github.com/fish-tennis/gnet)

support TcpSocket and WebSocket

# c# project example
```c#
public class TestClient
    {
        public TcpConnection m_Connection;

        public TestClient()
        {
            var connectionConfig = new ConnectionConfig
            {
                RecvBufferSize = 1024 * 100,
                RecvTimeout = 1000,
                WriteTimeout = 1000
            };
            var codec = new ProtoCodec();
            connectionConfig.Codec = codec;
            m_Connection = new TcpConnection(connectionConfig, 1)
            {
                Tag = this,
                OnConnected = onConnected,
                OnClose = onClose,
            };

            codec.Register(Convert.ToUInt16(pb.CmdTest.CmdHeartBeat), pb.HeartBeatRes.Descriptor);
            codec.Register(Convert.ToUInt16(pb.CmdTest.Message), pb.TestMessage.Descriptor);
        }

        public void Start()
        {
            m_Connection.Connect("127.0.0.1:10002");
        }

        public void Stop()
        {
            m_Connection.Close();
        }

        private void onConnected(IConnection connection, bool success)
        {
            Console.WriteLine("onConnected host:"+ m_Connection.GetHostAddress() + " success:"+success);
        }

        private void onClose(IConnection connection)
        {
            Console.WriteLine("onClose");
        }
        
        public void ProcessPackets()
        {
            while (true)
            {
                var packet = m_Connection.PopPacket();
                if (packet == null)
                {
                    return;
                }
                // write your logic code here
                Console.WriteLine("recv cmd:"+packet.Command() + " msg:"+packet.Message());
            }
        }
    }
```

# unity example
```c#
public class test : MonoBehaviour
{
    private TcpConnection m_Connection;
    private float m_HeartBeatCounter;
    
    void Start()
    {
        var connectionConfig = new ConnectionConfig
        {
            RecvBufferSize = 1024 * 100,
            RecvTimeout = 1000,
            WriteTimeout = 1000,
            HeartBeatInterval = 5
        };
        var codec = new ProtoCodec();
        connectionConfig.Codec = codec;
        m_Connection = new TcpConnection(connectionConfig, 1)
        {
            OnConnected = onConnected,
            OnClose = onClose,
        };
        codec.Register(Convert.ToUInt16(pb.CmdTest.CmdHeartBeat), pb.HeartBeatRes.Descriptor);
        codec.Register(Convert.ToUInt16(pb.CmdTest.Message), pb.TestMessage.Descriptor);
    }

    // Update is called once per frame
    void Update()
    {
        processPackets();
    }

    void onConnected(IConnection connection, bool success)
    {
        Debug.LogFormat("onConnected:{0} host:{1}", success, m_Connection.GetHostAddress());
    }
    
    void onClose(IConnection connection)
    {
        Debug.LogFormat("onClode");
    }

    void processPackets()
    {
        while (m_Connection.IsConnected())
        {
            var packet = m_Connection.PopPacket();
            if (packet == null)
            {
                break;
            }
            // write your logic code here
            Debug.LogFormat("recv cmd:{0} msg:{1}", packet.Command(), packet.Message());
        }
        m_HeartBeatCounter += Time.deltaTime;
        if (m_Connection.GetConfig().HeartBeatInterval > 0 &&
            m_HeartBeatCounter > m_Connection.GetConfig().HeartBeatInterval)
        {
            m_HeartBeatCounter = 0;
            m_Connection.Send(Convert.ToUInt16(CmdTest.CmdHeartBeat), new HeartBeatReq
                {
                    Timestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
                }
            );
            Debug.LogFormat("ping");
        }
    }
}
```
