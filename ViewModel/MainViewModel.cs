using Microsoft.Win32;
using MyLogLib;
using Newtonsoft.Json;
using RabbitMQ;
using RabbitMQ.YQMsg;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;


namespace YQLaser.UI.ViewModel
{
    public class MainViewModel : ObservableObject
    {
        public MainViewModel()
        {
            LastMSN = SysCfg.LAST_CARVE_MSN;
            LaserConnected = false;

        }

        #region properties
        public Action<string> OnShowMsg { get; set; }

        /// <summary>
        /// 激光刻录机连接状态
        /// </summary>
        public bool LaserConnected
        {
            get => _laserConnected;
            set => Set(ref _laserConnected, value);
        }

        /// <summary>
        /// 当前心跳状态
        /// </summary>
        public HeartStatus CurrHeart { get => _CurrHeart; set => Set(ref _CurrHeart, value); }

        /// <summary>
        /// 当前制令等数据
        /// </summary>
        public TaskData TaskData { get => _TaskData; set => Set(ref _TaskData, value); }

        /// <summary>
        /// 当前结论数据
        /// </summary>
        public RltData ResultData { get => _ResultData; set => Set(ref _ResultData, value); }

        /// <summary>
        /// 当前厂内码
        /// </summary>
        public string CurrFactoryCode { get => _CurrFactoryCode; set => Set(ref _CurrFactoryCode, value); }

        /// <summary>
        /// 当前GUID
        /// </summary>
        public string CurrGUID { get => _CurrGUID; set => Set(ref _CurrGUID, value); }

        /// <summary>
        /// 当前电表结论
        /// </summary>
        public string CurrMeterRlt { get => _CurrMeterRlt; set => Set(ref _CurrMeterRlt, value); }

        /// <summary>
        /// 当前刻录结果
        /// </summary>
        public string CurrCarveRlt { get => _CurrCarveRlt; set => Set(ref _CurrCarveRlt, value); }

        /// <summary>
        /// 当前MSN
        /// </summary>
        public string CurrMSN { get => _CurrMSN; set => Set(ref _CurrMSN, value); }

        public string LastMSN { get => _LastMSN; set => Set(ref _LastMSN, value); }

        /// <summary>
        /// PLC值
        /// </summary>
        public string PLC_VALUE { get => _PLC_VALUE; set => Set(ref _PLC_VALUE, value); }
        #endregion

        #region fields
        private string _PLC_VALUE;
        private string _LastMSN;
        private RltData _ResultData;
        private string _CurrMSN;
        private string _CurrMeterRlt;
        private string _CurrCarveRlt;
        private string _CurrGUID;
        private string _CurrFactoryCode;
        private bool _laserConnected;
        private ClientMQ mqClient;
        private HeartStatus _CurrHeart = HeartStatus.Initializing;
        private Socket socket;
        private bool isBusy = false;
        private TaskData _TaskData;
        private CancellationTokenSource tokenSource;
        private List<string> lstMSN = new List<string>();
        private readonly object objlock = new { };
        #endregion

        /// <summary>
        /// 先连接激光刻录，成功后连接MQ
        /// </summary>
        private void Init()
        {
            CurrHeart = HeartStatus.Initializing;
            var rltLayser = InitLaser();
            if (!rltLayser)
            {
                return;
            }
            InitMQ();
        }

        private bool InitMQ()
        {
            ShowMsg("初始化MQ...");
            mqClient?.Close();
            try
            {
                mqClient = new ClientMQ();
                mqClient.singleArrivalEvent += MqClient_singleArrivalEvent;
                mqClient.ReceiveMessage();
                ShowMsg("初始化MQ完毕！");
                //心跳开始
                HeartBeat();
                return true;
            }
            catch (Exception ex)
            {
                MyLog.WriteLog("初始化MQ失败！", ex);
                ShowMsg("初始化MQ失败！");
            }
            return false;
        }

        private bool InitLaser()
        {
            DisConnect();
            try
            {
                Task.Run(() =>
                {
                    while (true)
                    {
                        LaserConnected = socket != null && socket.Connected;
                        Thread.Sleep(1500);
                    }
                });
                socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                socket.SendTimeout = 2000;
                socket.ReceiveTimeout = 5000;//
                socket.Connect(new IPEndPoint(IPAddress.Parse(SysCfg.SERVER_IP), SysCfg.SERVER_PORT));
                return true;
            }
            catch (Exception ex)
            {
                MyLog.WriteLog("连接刻录机失败!", ex);
                ShowMsg("连接刻录机失败!");
                return false;
            }
        }

        private void DisConnect()
        {
            if (socket?.Connected == false)
            {
                socket.Disconnect(false);
            }
        }

        public void MqClient_singleArrivalEvent(string data)
        {
            MyLog.WriteLog("收到信息 -- " + data);
            ShowMsg(data);
            MsgBase msg = null;
            try
            {
                msg = JsonConvert.DeserializeObject<MsgBase>(data);
            }
            catch (Exception ex)
            {
                string errMsg = "协议格式错误！";
                MyLog.WriteLog(errMsg, ex);
                ShowMsg(errMsg);
                return;
            }
            DateTime dtMsg;
            if (!DateTime.TryParse(msg.time_stamp, out dtMsg))
            {
                ShowMsg("时间戳错误，不处理此消息！");
                MyLog.WriteLog("时间戳错误，不处理此消息!");
                return;
            }
            if ((DateTime.Now - dtMsg).TotalSeconds > 120)
            {
                ShowMsg("2分钟前的消息不处理！");
                MyLog.WriteLog("2分钟前的消息不处理！");
                return;
            }
            if (CurrHeart == HeartStatus.Working)
            {
                ShowMsg("工作中，消息不处理！");
                MyLog.WriteLog("工作中，消息不处理！");
                return;
            }
            try
            {
                //lock (objlock)
                {
                    if (msg.MESSAGE_TYPE == "task")
                    {
                        Task.Run(() =>
                        {
                            AnalyseTaskMsg(data);
                        });
                    }
                    else if (msg.MESSAGE_TYPE == "execute")
                    {
                        Task.Run(() =>
                        {
                            AnalyseExecuteMsg(data);
                        });
                    }
                    else { }
                }
            }
            catch (Exception ex)
            {
                MyLog.WriteLog(ex);
                ShowMsg(ex.Message + "\r\n" + ex.StackTrace);
            }
        }

        #region 分析MQ
        private void AnalyseTaskMsg(string data)
        {
            var tskMsg = JsonConvert.DeserializeObject<TaskMsg>(data);
            this.TaskData = tskMsg.DATA;
            if (TaskData == null || string.IsNullOrEmpty(TaskData.WORKORDER_CODE))
            {
                CurrHeart = HeartStatus.Error;
                MyLog.WriteLog("主控下发的制令错误，空制令号!");
                ShowMsg("主控下发的制令错误，空制令号!");
            }
            else
            {
                ShowMsg($"收到制令{TaskData.WORKORDER_CODE}!");
                CurrHeart = HeartStatus.Init_Complete;
            }
        }

        private void AnalyseExecuteMsg(string data)
        {
            var execMsg = JsonConvert.DeserializeObject<ExecuteMsg>(data);
            this.ResultData = execMsg.DATA.FirstOrDefault();
            if (this.ResultData == null || string.IsNullOrEmpty(this.ResultData.BAR_CODE))
            {
                //上报故障，数据异常
                CurrHeart = HeartStatus.Error;
                MyLog.WriteLog("主控下发的启动命令错误，无厂内码!");
                ShowMsg("主控下发的启动命令错误，无厂内码!");
                return;
            }
            if (CurrHeart == HeartStatus.Error || CurrHeart == HeartStatus.Initializing
                || CurrHeart == HeartStatus.Maintenance || CurrHeart == HeartStatus.Working)
            {
                MyLog.WriteLog($"状态为{CurrHeart},不执行!");
                ShowMsg($"状态为{CurrHeart},不执行!");
                return;
            }
            //未完成，未初始化结束不执行
            if (CurrHeart != HeartStatus.Finished && CurrHeart != HeartStatus.Init_Complete)
            {
                MyLog.WriteLog("未完成，未初始化结束不执行!");
                ShowMsg("未完成，未初始化结束不执行!");
                return;
            }
            CurrFactoryCode = this.ResultData.BAR_CODE;
            //查询初调、复校合格结果
            string sqlChuTiao = $"select * from RESULT where RESULT=0 and BAR_CODE='{CurrFactoryCode}' and DEVICE_TYPE='E021';";
            string sqlFuJiao = $"select* from RESULT where RESULT = 0 and BAR_CODE = '{CurrFactoryCode}' and DEVICE_TYPE='E022'; ";
            SqlHelper sql = new SqlHelper(SysCfg.MASTER_DB_CONNSTR);
            if (!sql.Open())
            {
                MyLog.WriteLog("主控数据库连接失败！");
                ShowMsg("主控数据库连接失败！");
                return;
            }
            var dtChuTiao = sql.SelectData(sqlChuTiao);
            if (dtChuTiao == null || dtChuTiao.Rows.Count == 0)
            {
                MyLog.WriteLog($"{CurrFactoryCode}未查询到初调合格数据！");
                ShowMsg($"{CurrFactoryCode}未查询到初调合格数据！");
                return;
            }
            if (!sql.Open())
            {
                MyLog.WriteLog("主控数据库连接失败！");
                ShowMsg("主控数据库连接失败！");
                return;
            }
            var dtFuJiao = sql.SelectData(sqlFuJiao);
            sql.Close();
            if (dtFuJiao == null || dtFuJiao.Rows.Count == 0)
            {
                MyLog.WriteLog($"{CurrFactoryCode}未查询到复校合格数据！");
                ShowMsg($"{CurrFactoryCode}未查询到复校合格数据！");
                return;
            }
            //查询GUID
            CurrGUID = GetGUID(CurrFactoryCode);
            if (string.IsNullOrEmpty(CurrGUID))
            {
                ResultData.result = "1";
                //查不到GUID，不刻录，上传失败结果
                CurrHeart = HeartStatus.Finished;
                ShowMsg($"厂内码{CurrFactoryCode}查询不到GUID，不刻录!");
                MyLog.WriteLog($"厂内码{CurrFactoryCode}查询不到GUID，不刻录!");
                DataMsg dataMsg = new DataMsg()
                {
                    NO = SysCfg.NO,
                    DEVICE_TYPE = SysCfg.DEVICE_TYPE,
                    DATA = new List<RltData>() { ResultData },
                };
                try
                {
                    mqClient.SentMessage(JsonConvert.SerializeObject(dataMsg));
                }
                catch (Exception ex)
                {
                    CurrHeart = HeartStatus.Error;
                    //MyLog.WriteLog("上传结论到服务器失败！", ex);
                    ShowMsg("上传结论到服务器失败！" + ex.Message);
                }
                return;
            }
            //判断之前结论是否合格
            if (this.ResultData.result == "1")
            {
                CurrMeterRlt = "不合格";
                CurrCarveRlt = "不刻录";
                //直接上传结论
                DataMsg dataMsg = new DataMsg()
                {
                    NO = SysCfg.NO,
                    DEVICE_TYPE = SysCfg.DEVICE_TYPE,
                    DATA = new List<RltData>() { ResultData },
                };
                try
                {
                    mqClient.SentMessage(JsonConvert.SerializeObject(dataMsg));
                }
                catch (Exception ex)
                {
                    CurrHeart = HeartStatus.Error;
                    //MyLog.WriteLog("上传结论到服务器失败！", ex);
                    ShowMsg("上传结论到服务器失败！" + ex.Message);
                }
            }
            else
            {
                CurrMeterRlt = "合格";
                CurrCarveRlt = "刻录中...";

                CurrMSN = lstMSN[SysCfg.LAST_CARVE_LINE + 1];
                CurrHeart = HeartStatus.Working;
                bool rlt = Carve(CurrMSN, CurrGUID);
                if (rlt)
                {
                    ResultData.result = "0";
                    CurrCarveRlt = "成功";
                }
                else
                {
                    ResultData.result = "1";
                    CurrCarveRlt = "失败";
                }
                try
                {
                    //上传MQ
                    DataMsg dataMsg = new DataMsg()
                    {
                        NO = SysCfg.NO,
                        DEVICE_TYPE = SysCfg.DEVICE_TYPE,
                        DATA = new List<RltData>() { ResultData }
                    };
                    string msg = JsonConvert.SerializeObject(dataMsg);
                    ShowMsg("上传MQ=>" + msg);
                    MyLog.WriteLog("上传MQ=>" + msg);
                    mqClient.SentMessage(msg);
                }
                catch (Exception ex)
                {
                    ShowMsg("上传MQ失败!" + ex.Message);
                    //MyLog.WriteLog("上传MQ失败!", ex);
                }
                CurrHeart = HeartStatus.Finished;
                if (SysCfg.LAST_CARVE_LINE + 1 >= lstMSN.Count)
                {
                    CurrMSN = "已用完！";
                    CurrHeart = HeartStatus.Error;
                    ShowMsg($"MSN已经用完!");
                    MyLog.WriteLog($"MSN已经用完!");
                    return;
                }
                CurrMSN = lstMSN[SysCfg.LAST_CARVE_LINE + 1];
            }
        }

        private PLCHelper plc;

#if IO_TRIGGER
        private bool Carve(string msn, string guid)
        {
            string strData = string.Format("02;{0:X2}{1:X2};{2}{3}", msn.Length, guid.Length, msn, guid.ToUpper());
            //发送给激光刻录机
            try
            {
                DisConnect();//重新连接，以防缓存数据干扰
                socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                socket.SendTimeout = 2000;
                socket.ReceiveTimeout = 5000;//
                socket.Connect(new IPEndPoint(IPAddress.Parse(SysCfg.SERVER_IP), SysCfg.SERVER_PORT));

                byte[] sendbyte = CSend(strData);
                ShowMsg($"厂内码:{CurrFactoryCode}  MSN:{CurrMSN}  GUID:{CurrGUID}");
                ShowMsg("发送给激光刻录机=>" + Encoding.ASCII.GetString(sendbyte));
                MyLog.WriteLog("发送给激光刻录机=>" + Encoding.ASCII.GetString(sendbyte));

                ConfigurationUtil.SetConfiguration("LAST_CARVE_LINE", (SysCfg.LAST_CARVE_LINE + 1).ToString());
                ConfigurationUtil.SetConfiguration("LAST_CARVE_MSN", CurrMSN);
                LastMSN = SysCfg.LAST_CARVE_MSN;
                MyLog.WriteLog($"厂内码:{CurrFactoryCode}MSN:{CurrMSN}GUID:{CurrGUID}", "CARVE");
                int tryTimes = 3;//20
                string rcvAnwser = "";
                do
                {
                    int len = socket.Send(sendbyte);
                    ShowMsg($"发送完毕{tryTimes}！");
                    Thread.Sleep(1000);
                    rcvAnwser = ReceiveLaser();
                } while (rcvAnwser != "20" && --tryTimes > 0);//"30"=刻录完成  "20"=回复

                if (rcvAnwser == "20")//刻录机收到数据应答
                {
                    ShowMsg("<=刻录机应答20，收到刻录数据!");
                    MyLog.WriteLog("<=刻录机应答20，收到刻录数据!");
                    PLC_VALUE = "";
                    //触发PLC刻录
                    plc?.DisConnect();
                    plc = new PLCHelper(SysCfg.PLC_IP, SysCfg.PLC_PORT);
                    plc.OnShowMsg += ShowMsg;
                    if (!plc.Connect())
                    {
                        ShowMsg("PLC连接失败！");
                        return false;
                    }
                    PLCResponse writeResp = plc.SetOnePoint(SysCfg.CARVE_ADDR, 3);
                    if (writeResp.HasError)
                    {
                        ShowMsg("PLC连接失败！");
                        return false;
                    }
                    //等待PLC刻录完成信号
                    while (true)
                    {
                        Thread.Sleep(2000);
                        PLCResponse resp = plc.ReadOnePoint(SysCfg.CARVE_ADDR);
                        if (resp.HasError)
                        {
                            ShowMsg("PLC读取失败！");
                            return false;
                        }
                        PLC_VALUE = resp.Text;
                        if (Convert.ToInt32(resp.Text) == 4)
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    ShowMsg("刻录机未应答【20】，刻录失败!");
                    MyLog.WriteLog("刻录机未应答【20】，刻录失败!");
                    return false;
                }
            }
            catch (Exception ex)
            {
                MyLog.WriteLog("发送至激光刻录机失败！", ex);
                ShowMsg("发送至激光刻录机失败！");
                return false;
            }
        }
#else
        private bool Carve(string msn, string guid)
        {
            string strData = string.Format("02;{0:X2}{1:X2};{2}{3}", msn.Length, +guid.Length, msn, guid.ToUpper());
            //发送给激光刻录机
            try
            {
                if (socket?.Connected != true)
                {
                    InitLaser();
                }
                byte[] sendbyte = CSend(strData);
                ShowMsg($"厂内码:{CurrFactoryCode}  MSN:{CurrMSN}  GUID:{CurrGUID}");
                ShowMsg("发送给激光刻录机=>" + Encoding.ASCII.GetString(sendbyte));
                MyLog.WriteLog("发送给激光刻录机=>" + Encoding.ASCII.GetString(sendbyte));
                int len = socket.Send(sendbyte);
                MyLog.WriteLog($"厂内码:{CurrFactoryCode}MSN:{CurrMSN}GUID:{CurrGUID}", "CARVE");
                ShowMsg("发送完毕！");
                ConfigurationUtil.SetConfiguration("LAST_CARVE_LINE", (SysCfg.LAST_CARVE_LINE + 1).ToString());
                ConfigurationUtil.SetConfiguration("LAST_CARVE_MSN", CurrMSN);
                LastMSN = SysCfg.LAST_CARVE_MSN;
                int tryTimes = 5;
                string rcvAnwser = "";
                do
                {
                    rcvAnwser = ReceiveLaser();
                    Thread.Sleep(3000);
                } while (rcvAnwser != "30" && --tryTimes > 0);//"30"=刻录完成
                if (rcvAnwser == "30")//完成
                {
                    return true;
                }
                else//未完成，且超时
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                MyLog.WriteLog("发送至激光刻录机失败！", ex);
                ShowMsg("发送至激光刻录机失败！");
                return false;
            }
        }
#endif
        #endregion

        private string GetGUID(string factoryCode)
        {
            string guid = "";
            try
            {
                SqlHelper sql = new SqlHelper(SysCfg.SQLCONN);
                string strSQL = "select GUID from t_mcodelink where innerID='" + factoryCode + "' order by LinkTime2 desc,LinkTime1 desc";
                DataTable dt = sql.SelectData(strSQL);//根据厂内码查询GUID
                if (dt != null && dt.Rows.Count > 0)
                {
                    //查询回来的GUID = 1C234F00000403C1，需要增加横线1C-23-4F-00-00-04-03-C1
                    guid = dt.Rows[0][0].ToString();
                    guid = string.Join("-", Regex.Matches(guid, "..").Cast<Match>());
                }
            }
            catch (Exception ex)
            {
                guid = "";
                MyLog.WriteLog("查询GUID失败！", ex);
                ShowMsg("查询GUID失败！");
            }
            return guid;
        }

        #region 激光刻录协议
        private byte[] CSend(string data)
        {
            int sendStringLength = data.Length;
            string sendString = "0068" + SysCfg.CLIENT_PORT + sendStringLength.ToString("X2") + "10" + data;
            byte[] buffer = Encoding.ASCII.GetBytes(sendString);
            List<byte> sendByte = new List<byte>();
            sendByte.AddRange(buffer);

            byte cs = CSCalculate(buffer, 0, buffer.Length);
            sendByte.AddRange(Encoding.ASCII.GetBytes(cs.ToString("X2")));
            sendByte.AddRange(Encoding.ASCII.GetBytes("1600"));
            return sendByte.ToArray();
        }
        public byte CSCalculate(byte[] hexbyte, int startIndex, int length)
        {
            try
            {
                if ((hexbyte.Length == 0))//判断数组是否为空
                {
                    return 0;
                }
                if (startIndex + length > hexbyte.Length)
                {
                    return 0;
                }
                int result = 0;
                for (int i = startIndex; i < length + startIndex; i++)
                {
                    result += hexbyte[i];
                }
                while (true)
                {
                    if (result < 0x100)
                    {
                        break;
                    }
                    result = result - 0x100;
                    Thread.Sleep(100);
                }
                return Convert.ToByte(result);
            }
            catch (Exception ex)
            {
                //sl.ExceptionWrite(ex.ToString());
                return 0;
            }
        }

        /// <summary>
        /// 返回应答的报文类型
        /// </summary>
        /// <returns></returns>
        public string ReceiveLaser()
        {
            try
            {
                byte[] buffer = new byte[1024];
                int len = socket.Receive(buffer);
                byte[] rcvData = buffer.Take(len).ToArray();
                string strData = Encoding.ASCII.GetString(rcvData);
                MyLog.WriteLog("<=接收刻录机:" + strData);
                //分析接收的数据任务代码
                bool tt = RecieveAnalyze(strData, out string control, out string answerType, out int number, out string data);
                if (tt)
                {
                    return answerType;
                }
                else
                {
                    return "";
                }
                #region 注释
                /*
                if (strData == "889")
                {
                    Console.WriteLine("客户端收到了889");
                    continue;
                }
                if (answerType == "20")
                {
                    Log.WriteLog("接收自激光刻录机<=" + rec);
                    Log.WriteLog("刻录机收到刻录内容：");
                    continue;
                }
                if (answerType == "30")
                {
                    Log.WriteLog("接收自激光刻录机<=" + rec);
                    Log.WriteLog("刻录机反馈刻录结果：");
                    if (true)//如果全部刻录完毕
                    {
                        this.Invoke(new Action(() =>
                        {
                            txtUpperShellCode.Clear();
                            lblLastJuBian.Text = lblCurrentMSN.Text;
                            ++numBurnedCounter;
                            if (numBurnedCounter > fileLines.Length - 1)
                            {
                                numBurnedCounter = numBurnedCounter - 1;
                                lblCurrentMSN.Text = fileLines[numBurnedCounter];
                                MessageBox.Show("本制令号局编文件范围已刻录完毕！");
                                Log.WriteLog("本制令号局编文件范围已刻录完毕！");
                                return;
                            }
                            lblCurrentMSN.Text = fileLines[numBurnedCounter];
                        }));
                    }
                    continue;
                }*/
                #endregion
            }
            catch (Exception ex)
            {
                MyLog.WriteLog("应答错误!", ex);
                return "";
            }
        }
        private bool RecieveAnalyze(string rec, out string control, out string answerType, out int num, out string data)//分析接收的数据任务代码
        {
            control = "";
            answerType = "";
            num = 0;
            data = "";
            //0068 9090 2B 20 02;123456789001010708090a01020304 34 1600
            if (rec.Length > 12)
            {
                try
                {
                    control = rec.Substring(8, 2);
                    answerType = rec.Substring(10, 2);
                    num = Convert.ToInt32(rec.Substring(12, 2));
                    int num1 = Convert.ToInt32(rec.Substring(15, 2), 16) / 2;
                    int num2 = Convert.ToInt32(rec.Substring(17, 2), 16) / 2;
                    string dataN = rec.Substring(rec.LastIndexOf(';') + 1);
                    data = dataN.Substring(0, num1);
                    data += " ";
                    data += dataN.Substring(num1, num2);
                    return true;
                }
                catch (Exception ex)
                {
                    MyLog.WriteLog("报文解析错误！", ex);
                    return false;
                }
            }
            return false;
        }
        #endregion

        #region 心跳
        private void HeartBeat()
        {
            tokenSource?.Cancel();
            tokenSource = new CancellationTokenSource();
            Task.Run(() =>
            {
                while (!tokenSource.IsCancellationRequested)
                {
                    try
                    {
                        HeartBeatMsg msg = new HeartBeatMsg()
                        {
                            DEVICE_TYPE = SysCfg.DEVICE_TYPE,
                            NO = SysCfg.NO,
                            STATUS = ((int)CurrHeart).ToString()
                        };
                        string strMsg = JsonConvert.SerializeObject(msg);
                        mqClient.SentMessage(strMsg);
                    }
                    catch (Exception ex)
                    {
                        ShowMsg("心跳上报失败!" + ex.Message);
                    }
                    Thread.Sleep(SysCfg.HEARTBEAT_TIMESPAN);
                }
            }, tokenSource.Token);
        }
        #endregion

        #region ConnectCmd
        private RelayCommand _ConnectCmd;
        public RelayCommand ConnectCmd => _ConnectCmd ?? (_ConnectCmd = new RelayCommand(Connect, () => !isBusy));

        private async void Connect()
        {
            FetchMSNFromFile(SysCfg.MSN_FILE_PATH);
            if (lstMSN.Count == 0)
            {
                ShowMsg("请先选择MSN文件!");
                return;
            }
            isBusy = true;
            await Task.Run(new Action(Init));
            isBusy = false;
        }
        #endregion

        #region SelectMSNFileCmd
        private RelayCommand _SelectMSNFileCmd;
        public RelayCommand SelectMSNFileCmd => _SelectMSNFileCmd ?? (_SelectMSNFileCmd = new RelayCommand(SelectMSNFile));

        private void SelectMSNFile()
        {
            int remainCount = lstMSN.Count - 1 - SysCfg.LAST_CARVE_LINE;
            if (remainCount > 0)
            {
                if (System.Windows.MessageBox.Show($"当前文件MSN剩余{remainCount}个，是否更换文件？", "提示", System.Windows.MessageBoxButton.YesNo) != System.Windows.MessageBoxResult.Yes)
                {
                    return;
                }
            }

            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "*.txt|*.txt";
            dialog.Multiselect = false;
            var rlt = dialog.ShowDialog();
            if (!rlt.Value)
            {
                return;
            }
            ConfigurationUtil.SetConfiguration("LAST_CARVE_LINE", "-1");
            ConfigurationUtil.SetConfiguration("MSN_FILE_PATH", dialog.FileName);
            FetchMSNFromFile(SysCfg.MSN_FILE_PATH);
        }

        /// <summary>
        /// 从文件取MSN
        /// </summary>
        /// <param name="msnFilePath"></param>
        private void FetchMSNFromFile(string msnFilePath)
        {
            try
            {
                if (!File.Exists(msnFilePath))
                {
                    MyLog.WriteLog("MSN文件不存在!" + msnFilePath);
                    ShowMsg("MSN文件不存在!" + msnFilePath);
                    return;
                }
                lstMSN = new List<string>();
                FileStream fs = new FileStream(msnFilePath, FileMode.Open);
                StreamReader sr = new StreamReader(fs);
                while (!sr.EndOfStream)
                {
                    string msn = sr.ReadLine();
                    if (msn?.Length > 2)//TODO:MSN长度过滤
                    {
                        lstMSN.Add(msn);
                    }
                }
                sr.Close();
                sr.Dispose();
                fs.Close();
                fs.Dispose();
                //判断是否长度合理
                if (SysCfg.LAST_CARVE_LINE + 1 >= lstMSN.Count)
                {
                    MyLog.WriteLog($"错误，上次刻录行号（0开始）{SysCfg.LAST_CARVE_LINE}超出文件中MSN数量{lstMSN.Count}!");
                    ShowMsg($"错误，上次刻录行号（0开始）{SysCfg.LAST_CARVE_LINE}超出文件中MSN数量{lstMSN.Count}!");
                    lstMSN.Clear();
                }
                //设置当前MSN
                CurrMSN = "";
                if (lstMSN.Count > 0)
                {
                    CurrMSN = lstMSN[SysCfg.LAST_CARVE_LINE + 1];
                }
            }
            catch (Exception ex)
            {
                CurrHeart = HeartStatus.Error;
                MyLog.WriteLog("MSN文件读取异常！", ex);
                ShowMsg("MSN文件读取异常！");
            }
        }
        #endregion

        #region SetPLCCarveCmd
        private RelayCommand _SetPLCCarveCmd;
        public RelayCommand SetPLCCarveCmd => _SetPLCCarveCmd ?? (_SetPLCCarveCmd = new RelayCommand(SetPLCCarve));
        void SetPLCCarve()
        {
            if (System.Windows.MessageBox.Show("是否启动刻录？","警告", System.Windows.MessageBoxButton.YesNo) != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }
            PLCHelper plc = new PLCHelper(SysCfg.PLC_IP, SysCfg.PLC_PORT);
            plc.OnShowMsg += ShowMsg;
            if (plc.Connect())
            {
                var resp = plc.SetOnePoint(SysCfg.CARVE_ADDR, 3);
                ShowMsg(resp.HasError ? resp.ErrorMsg : resp.Text);
            }
            plc.DisConnect();
        }
        #endregion

        private void ShowMsg(string msg)
        {
            try
            {
                OnShowMsg?.Invoke(msg);
            }
            catch (Exception ex)
            {
                MyLog.WriteLog("OnShowMsg委托调用异常！", ex);
            }
        }
    }
}
