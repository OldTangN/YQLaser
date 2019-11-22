using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YQLaser.UI
{
    public class SysCfg
    {
        /// <summary>
        /// 设备类型
        /// </summary>
        public static string DEVICE_TYPE => ConfigurationUtil.GetConfiguration(Convert.ToString, () => "E024");

        /// <summary>
        /// 设备类型
        /// </summary>
        public static string NO => ConfigurationUtil.GetConfiguration(Convert.ToString, () => "E02401");

        /// <summary>
        /// 刻录机IP
        /// </summary>
        public static string SERVER_IP => ConfigurationUtil.GetConfiguration(Convert.ToString, () => "");
        
        /// <summary>
        /// 刻录机IP
        /// </summary>
        public static string SQLCONN => ConfigurationUtil.GetConfiguration(Convert.ToString, () => "");
        
        /// <summary>
        /// 刻录机端口
        /// </summary>
        public static int SERVER_PORT => ConfigurationUtil.GetConfiguration(int.Parse, () => 6060);

        /// <summary>
        /// 本机端口
        /// </summary>
        public static int CLIENT_PORT => ConfigurationUtil.GetConfiguration(int.Parse, () => 9090);

        /// <summary>
        /// 上次刻录行序号
        /// <para>默认-1</para>
        /// </summary>
        public static int LAST_CARVE_LINE => ConfigurationUtil.GetConfiguration(int.Parse, () => -1);

        /// <summary>
        /// 上次刻录行序号
        /// <para>默认-1</para>
        /// </summary>
        public static string LAST_CARVE_MSN => ConfigurationUtil.GetConfiguration(Convert.ToString, () => "");


        /// <summary>
        /// MSN文件路径
        /// </summary>
        public static string MSN_FILE_PATH=> ConfigurationUtil.GetConfiguration(Convert.ToString, () => "");
        /// <summary>
        /// 心跳间隔
        /// <para>默认1000ms</para>
        /// </summary>
        public static int HEARTBEAT_TIMESPAN => ConfigurationUtil.GetConfiguration(int.Parse, () => 1000);

        /// <summary>
        /// 主控数据库连接
        /// </summary>
        public static string MASTER_DB_CONNSTR => ConfigurationUtil.GetConfiguration(Convert.ToString, () => "");


        public static bool SetConfiguration(string key, object val)
        {
            return ConfigurationUtil.SetConfiguration(key, val.ToString());
        }
    }
}
