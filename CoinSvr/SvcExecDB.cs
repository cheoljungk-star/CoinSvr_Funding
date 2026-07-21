//INSTANT C# NOTE: Formerly VB project-level imports:
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

using System.Threading;
using System.Text;
using System.Runtime.InteropServices;
using System.Net.Sockets;
using System.Net;
using CoinSvr;

namespace CoinSvr
{
    public class SvcExecDB
    {
        public SvcExecDB()
        {
            try
            {
                Task t1 = new Task(new Action(getEvent));
                t1.Start();
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("[" + this.ToString() + "] SvcExecDB", ex);
            }

        }
        public async void getEvent()
        {
            while (true)
            {
                try
                {
                    bool shouldSleep = true;
                    lock (Ob.db_lock)
                    {
                        if (Ob.db_exec_Queue.Count != 0)
                        {
                            shouldSleep = false;
                        }
                    }
                    if (shouldSleep)
                    {
                        await Task.Delay(1);
                    }
                    else
                    {
                        List<string> ourQueue;
                        lock (Ob.db_lock)
                        {
                            ourQueue = Ob.db_exec_Queue;
                            Ob.db_exec_Queue = new List<string>();
                        }
                        foreach (var data in ourQueue)
                        {
                            this.analysisRecv(data);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Ob.app._ERROR("getEvent In", ex);
                }
            }
        }
        public async void analysisRecv(string Recv)
        {
            try
            {
                await Ob.db.ExecuteQueryAsync(Recv, false);
            }
            catch (Exception ex)
            {
                Ob.app._ERROR("[" + this.ToString() + "] analysisRecv", ex);
            }
        }
    }

}