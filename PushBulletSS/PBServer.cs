using System;
using System.Text;
using Crestron.SimplSharp;
using Crestron.SimplSharp.Net.Http;
using Crestron.SimplSharp.Net.Https;
using Crestron.SimplSharp.CrestronWebSocketClient;
using Newtonsoft.Json;                          				// For Basic SIMPL# Classes

namespace PushBulletSS {
    public class PBServer {
        string apiToken;

        /// <summary>
        /// SIMPL+ can only execute the default constructor. If you have variables that require initialization, please
        /// use an Initialize method
        /// </summary>
        public PBServer() {
        }

        public delegate void onCallback();
        public onCallback onEventCompleted { get; set; }
        public onCallback onResponseUnlock { get; set; }
        public onCallback onResponseIgnore { get; set; }

        public delegate void onStatusD(ushort online, SimplSharpString status);
        public onStatusD onStatus { get; set; }

        bool online;
        const string api = "https://api.pushbullet.com/v2";

        private ushort cbool(bool b) { return (ushort)(b ? 1 : 0); }

        private void debug(string msg) {
            CrestronConsole.PrintLine(msg);
            if (msg.Length > 200) msg = msg.Substring(0, 200);
            onStatus(cbool(online), msg);
        }

        public void Initialize(SimplSharpString setToken) {
            apiToken = setToken.ToString();
            debug("PBServer.Initialize");

            checkPushes();
            openWebsocket();
        }

        double lastModified = 0;

        private void checkPushes() {
            debug("checking for new data since " + lastModified);
            HttpsClient cli = new HttpsClient();
            cli.KeepAlive = false;
            //cli.Verbose = true;

            HttpsClientRequest req = new HttpsClientRequest();
            req.Url.Parse(api + "/pushes?modified_after=" + lastModified);
            req.Header.AddHeader(new HttpsHeader("Access-Token", apiToken));
            cli.DispatchAsync(req, (resp, e) => {
                try {
                    if (resp.Code != 200) {
                        debug("Bad API Token? " + resp.Code + ": " + resp.ContentString);
                        return;
                    }
                    PushesData ps = JsonConvert.DeserializeObject<PushesData>(resp.ContentString);
                    foreach (PushData p in ps.pushes) {
                        handlePush(p);
                        if (p.modified > lastModified) lastModified = p.modified;
                    }
                    debug("lastModified is now " + lastModified);
                } catch (Exception e2) {
                    debug("GET failed: " + e2.Message);
                }
            });
        }

        public void Doorbell() {
            debug("PBServer.Doorbell");

            HttpsClient cli = new HttpsClient();
            cli.KeepAlive = false;
            //cli.Verbose = true;

            HttpsClientRequest req = new HttpsClientRequest();
            req.Url.Parse(api + "/pushes");
            req.Header.AddHeader(new HttpsHeader("Access-Token", apiToken));
            req.RequestType = Crestron.SimplSharp.Net.Https.RequestType.Post;
            req.Header.AddHeader(new HttpsHeader("Content-Type", "application/json"));
            PushData p = new PushData("Crestron Doorbell!", "Reply Unlock or Ignore.", "note");
            req.ContentString = JsonConvert.SerializeObject(p);

            cli.DispatchAsync(req, (resp, e) => {
                try {
                    if (resp.Code != 200) {
                        debug("Bad API Token? " + resp.Code + ": " + resp.ContentString);
                        return;
                    }
                    debug("Doorbell response: " + resp.ContentString);
                } catch (Exception e2) {
                    debug("POST failed: " + e2.Message);
                }
                onEventCompleted();
            });
        }

        WebSocketClient wsCli;
        private void openWebsocket() {
            // wss://stream.pushbullet.com/websocket/<your_access_token_here>
            try {
                wsCli = new WebSocketClient();
                wsCli.SSL = true;
                wsCli.Port = WebSocketClient.WEBSOCKET_DEF_SSL_SECURE_PORT;
                wsCli.URL = "wss://stream.pushbullet.com/websocket/" + apiToken;
                wsCli.ConnectionCallBack = connectWebsocket;
                wsCli.ReceiveCallBack = receiveWebsocket;
                wsCli.ConnectAsync();
            } catch (Exception e) {
                debug("Failed to set up websocket: " + e.Message);
            }
        }

        private int connectWebsocket(WebSocketClient.WEBSOCKET_RESULT_CODES err) {
            debug("websocket error = " + err);
            if (err == WebSocketClient.WEBSOCKET_RESULT_CODES.WEBSOCKET_CLIENT_SUCCESS) {
                online = true;
                debug("Connected");
                wsCli.ReceiveAsync();
            } else {
                online = false;
                debug("WebSocket offline");
            }

            return 0;
        }

        private int receiveWebsocket(byte[] data, uint len, WebSocketClient.WEBSOCKET_PACKET_TYPES opcode, WebSocketClient.WEBSOCKET_RESULT_CODES e) {
            string sdata = Encoding.ASCII.GetString(data, 0, (int)len);
            
            try {
                WSData msg = JsonConvert.DeserializeObject<WSData>(sdata);
                if (msg.type != "nop")
                    debug("websocket receive: " + sdata);

                if (msg.type == "tickle" && msg.subtype == "push")
                    checkPushes();
            } catch (Exception) {
                debug("unknown websocket data received");
            }
            wsCli.ReceiveAsync();

            return 0;
        }

        private void handlePush(PushData p) {
            if (p.body == null) return;
            debug("handle push: " + p.body);
            if (p.body.ToLower().StartsWith("unlock"))
                onResponseUnlock();
            else if (p.body.ToLower().StartsWith("ignore"))
                onResponseIgnore();
        }
    }
}
