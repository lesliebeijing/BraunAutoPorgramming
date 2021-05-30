using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace BraunAutoProgramming
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();

            string demoProposalXml = @"<ProposalList xmlns=""http://www.bbraun.com/HC/AIS/Space/AutoProgramming"">
                                     <Drug>
                                        <DrugId>201~0.25/50$3</DrugId>
                                        <DrugName>Lasix 0.25/50</DrugName>
                                        <DrugShort>0.005Lasix</DrugShort>
                                        <InfusionRate>0.20</InfusionRate>
                                        <RateUnit>ml/h</RateUnit>
                                        <Checksum>62583</Checksum>
                                     </Drug>
                                     <Drug>
                                       <DrugId>Insulin$4</DrugId>
                                        <DrugName>Insulin</DrugName>
                                        <InfusionRate>0.10</InfusionRate>
                                        <RateUnit>ml/h</RateUnit>
                                        <Checksum>52145</Checksum>
                                     </Drug>
                                    <ChecksumTotal>38110</ChecksumTotal>
                                    </ProposalList>";
            XElement root = XElement.Parse(demoProposalXml);
            txtProposalList.Text = root.ToString();

            label4.Text = "可下发一个或多个Drug到工作站,不用管Checksum可有可无,程序会自动处理Checksum";
        }

        private async void btnDownload_Click(object sender, EventArgs e)
        {
            string ip = txtIp.Text.Trim();
            int.TryParse(txtPort.Text.Trim(), out int port);
            if (ip.Length == 0 || port == 0)
            {
                return;
            }

            XNamespace aw = "http://www.bbraun.com/HC/AIS/Space/AutoProgramming";
            XElement root = XElement.Parse(txtProposalList.Text);
            IEnumerable<XElement> drugs = root.Elements(aw + "Drug");

            string crcString = "";
            foreach (var drug in drugs)
            {
                string s = "";
                foreach (var item in drug.Elements())
                {
                    if (item.Name != aw + "Checksum")
                    {
                        s += (string)item;
                    }
                }
                string crc = CRC16Util.calculate(s).ToString();
                crcString += crc;

                var checksumElement = drug.Element(aw + "Checksum");
                if (checksumElement == null)
                {
                    drug.Add(new XElement(aw + "Checksum", crc));
                }
                else
                {
                    checksumElement.SetValue(crc);
                }
            }

            string totalCrc = CRC16Util.calculate(crcString).ToString();
            var checkSumTotal = root.Element(aw + "ChecksumTotal");
            if (checkSumTotal == null)
            {
                root.Add(new XElement(aw + "ChecksumTotal", totalCrc));
            }
            else
            {
                checkSumTotal.SetValue(totalCrc);
            }

            txtProposalList.Text = root.ToString();

            try
            {
                btnDownload.Enabled = false;
                txtResponse.Text = "";
                var downloadTask = Download2SpaceComAsync(ip, port, root.ToString());
                var task = await Task.WhenAny(downloadTask, Task.Delay(5000));
                if (task == downloadTask)
                {
                    string resp = await downloadTask;
                    txtResponse.Text = resp;
                }
                else
                {
                    txtResponse.Text = "download timeout after 5 seconds";
                    // downloadTask.ContinueWith(t => txtResponse.Text = t.Result);
                }
                btnDownload.Enabled = true;
            }
            catch (Exception e1)
            {
                txtResponse.Text = e1.ToString();
                btnDownload.Enabled = true;
            }
        }

        private async Task<string> Download2SpaceComAsync(String ip, int port, string proposalXml)
        {
            TcpClient tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(ip, port);
            NetworkStream stream = tcpClient.GetStream();
            StreamWriter streamWriter = new StreamWriter(stream);
            StreamReader streamReader = new StreamReader(stream);
            await streamWriter.WriteAsync(proposalXml);
            string resp = await streamReader.ReadToEndAsync();
            tcpClient.Close();
            return resp;
        }

        /// <summary>
        /// 原生 socket 实现,仅供参考
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="port"></param>
        /// <param name="proposalXml"></param>
        /// <returns></returns>
        private async Task<string> Download2SpaceComAsync2(String ip, int port, string proposalXml)
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(IPAddress.Parse(ip), port);
            await socket.SendAsync(Encoding.UTF8.GetBytes(proposalXml), SocketFlags.None);

            byte[] buffer = new byte[1024 * 1024];
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                int len = await socket.ReceiveAsync(buffer, SocketFlags.None);
                string resp = Encoding.UTF8.GetString(buffer, 0, len);
                sb.Append(resp);
                if (len == -1)
                {
                    break;
                }
            }

            socket.Shutdown(SocketShutdown.Both);
            socket.Close();

            return sb.ToString();
        }
    }
}
