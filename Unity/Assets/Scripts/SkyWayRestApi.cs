using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UniRx;
using UnityEngine;
using MiniJSON;
using UnityEngine.UI;

//実際にSkyWay WebRTC Gatwayを操作するクラス
class RestApi
{
	//JSON Object定義
	[System.Serializable]
	class PeerOptions
	{
		public string key;
		public string domain;
		public string peer_id;
		public bool turn;
	}

	//JSON Object定義
	[System.Serializable]
	class VideoParams
	{
		//今回はSDP上にH264で接続希望するという趣旨を入れるためだけにこのパラメータを作る
		//ビデオを送り返すときはもうちょっとフィールドが増える
		public int band_width = 0;
		public string codec = "H264";
		public string media_id = "hoge";//ビデオを送り返すときはPOST /mediaで作成したIDをいれる
		public int payload_type = 96;
		public int sampling_rate = 90000;
	}

	//JSON Object定義
	[System.Serializable]
	class Constraints
	{
		public bool video = false;
		public bool videoReceiveEnabled = true;
		public bool audio = false;
		public bool audioReceiveEnabled = false;
		public VideoParams video_params = new VideoParams();
	}

	//JSON Object定義
	[System.Serializable]
	class RedirectParams
	{
		public Redirect video;
		//public Redirect audio;//audioを受信するときはこれも入れる
	}

	//JSON Object定義
	[System.Serializable]
	class Redirect
	{
		public string ip_v4;
		public ushort port;
	}

	//JSON Object定義
	[System.Serializable]
	class CallParams
	{
		public string peer_id;
		public string token;
		public string target_id;
		public Constraints constraints;
		public RedirectParams redirect_params;
	}

	//JSON Object定義
	[System.Serializable]
	class AnswerParams
	{
		public Constraints constraints;
		public RedirectParams redirect_params;
	}

	//SkyWay WebRTC GWを動かしているIPアドレスとポート番号
	const string entryPoint = "http://localhost:8000";

	//Peer Object生成タイミングでボタンを表示するためのイベント定義
	public delegate void OnOpenHandler();
	public event OnOpenHandler OnOpen;

	//動画が流れ始めたときにGUIを変更するためのイベント定義
	public delegate void OnStreamHandler();
	public event OnStreamHandler OnStream;
	
	private string _peerId;
	private string _peerToken;
	private string _media_connection_id;

	//POST /media/connections で渡すJSON Objectのパラメータ作成
	private CallParams _CreateCallParams(string targetId)
	{
		var videoRedirect = new Redirect();
		videoRedirect.ip_v4 = "127.0.0.1";
		videoRedirect.port = 7000;
		var redirectParams = new RedirectParams();
		redirectParams.video = videoRedirect;
		var constraints = new Constraints();
		var callParams = new CallParams();
		callParams.peer_id = _peerId;
		callParams.token = _peerToken;
		callParams.target_id = targetId;
		callParams.constraints = constraints;
		callParams.redirect_params = redirectParams;
		return callParams;
	}

	//POST /media/connections/{media_connection_id}/answer で渡すJSON Objectのパラメータ作成
	private AnswerParams _CreateAnswerParams()
	{
		var videoRedirect = new Redirect();
		videoRedirect.ip_v4 = "127.0.0.1";
		videoRedirect.port = 7000;
		var redirectParams = new RedirectParams();
		redirectParams.video = videoRedirect;
		var constraints = new Constraints();
		var answerParams = new AnswerParams();
		answerParams.constraints = constraints;
		answerParams.redirect_params = redirectParams;
		return answerParams;
	}

	//Unity側からの接続処理
	public void Call(string targetId)
	{
		var callParams = _CreateCallParams(targetId);
		string callParamsString = JsonUtility.ToJson(callParams);
		byte[] callParamsBytes = Encoding.UTF8.GetBytes(callParamsString);
		//SkyWay WebRTC GWのMediaStream確立用APIを叩く
		ObservableWWW.Post(entryPoint + "/media/connections", callParamsBytes).SelectMany(x =>
		{
			var response = Json.Deserialize(x) as Dictionary<string, object>;
			var parameters = (IDictionary) response["params"];
			_media_connection_id = (string) parameters["media_connection_id"];
			//この時点でSkyWay WebRTC GWは接続処理を始めている
			//発信側でやることはもうないが、相手側が応答すると自動で動画が流れ始めるため、
			//STREAMイベントを取って流れ始めたタイミングを確認しておくとボタン表示等を消すのに使える
			var url = string.Format("{0}/media/connections/{1}/events", entryPoint, _media_connection_id);
			return ObservableWWW.Get(url);
		}).Where(x =>
		{
			//STREAMイベント以外はいらないのでフィルタ
			var res = Json.Deserialize((string) x) as Dictionary<string, object>;
			return (string) res["event"] == "STREAM";
		}).First().Subscribe(//今回の用途だと最初の一回だけ取れれば良い
			x =>
			{
				//ビデオが正常に流れ始める
				//今回はmrayGStreamerUnityで受けるだけだが、ビデオを送り返したい場合はこのタイミングで
				//SkyWay WebRTC GW宛にRTPパケットの送信を開始するとよい
				OnStream();
				Debug.Log("video has beed started redirecting to " + callParams.redirect_params.video.ip_v4 + " " +
				          callParams.redirect_params.video.port);
			}, ex => { Debug.LogError(ex); });
	}

	//相手側からの接続があった場合に応答する処理
	//現状自動で無条件で接続確立している
	private void _Answer(string media_connection_id)
	{
		var answerParams = _CreateAnswerParams();
		string answerParamsString = JsonUtility.ToJson(answerParams);
		Debug.Log(answerParamsString);
		byte[] answerParamsBytes = Encoding.UTF8.GetBytes(answerParamsString);
		var url = string.Format("{0}/media/connections/{1}/answer", entryPoint, media_connection_id);
		//SkyWay WebRTC GWのMediaStream応答用APIを叩く
		ObservableWWW.Post(url, answerParamsBytes).SelectMany(x =>
		{
			//この時点でSkyWay WebRTC GWは接続処理を始めている
			//発信側でやることはもうないが、相手側が応答すると自動で動画が流れ始めるため、
			//STREAMイベントを取って流れ始めたタイミングを確認しておくとボタン表示等を消すのに使える
			//あと応答の場合はmedia_connection_idはもう知っているので別にJSONをParseする必要はない
			var eventUrl = string.Format("{0}/media/connections/{1}/events", entryPoint, media_connection_id);
			return ObservableWWW.Get(eventUrl);
		}).Where(x =>
		{
			//STREAMイベント以外はいらないのでフィルタ
			var res = Json.Deserialize((string) x) as Dictionary<string, object>;
			return (string) res["event"] == "STREAM";
		}).First().Subscribe(//今回の用途だと最初の一回だけ取れれば良い
			x =>
			{
				//ビデオが正常に流れ始める
				//今回はmrayGStreamerUnityで受けるだけだが、ビデオを送り返したい場合はこのタイミングで
				//SkyWay WebRTC GW宛にRTPパケットの送信を開始するとよい
				OnStream();
				Debug.Log("video has beed started redirecting to " + answerParams.redirect_params.video.ip_v4 + " " +
				          answerParams.redirect_params.video.port);
			}, ex => { Debug.LogError(ex); });
	}

	//終了処理
	public void Close()
	{
		var closeMediaURL = string.Format("{0}/media/connections/{1}", entryPoint, _media_connection_id);
		var closePeerURL = string.Format("{0}/peers/{1}?token={2}", entryPoint, _peerId, _peerToken);
		//DELETE MethodでAPIを叩くとSkyWay WebRTC GW内のオブジェクトが開放される
		//のだけど、ObservableWWWにDELETEメソッドが実装されていない…？？？
		//ObservableWWW.Delete(closeMediaURL);
		//ObservableWWW.Delete(closePeerURL);
	}
	
	//SkyWayサーバと繋がった時点での処理
	private void _OnOpen()
	{
		//UnityのGUI処理をするためにイベントを返してやる
		OnOpen();

		//イベントを監視する
		//今回は着呼イベントしか監視していないが、他にもDataChannel側の着信処理等のイベントも来る
		//これはプログラム起動中はずーっと監視しておくのが正しい。なのでRepeatする。
		var longPollUrl = string.Format("{0}/peers/{1}/events?token={2}", entryPoint, _peerId, _peerToken);
		ObservableWWW.Get(longPollUrl).OnErrorRetry((Exception ex) => { }).Repeat().Where(wx =>
		{
			Debug.Log(wx);
			var res = Json.Deserialize(wx) as Dictionary<string, object>;
			Debug.Log(res.ContainsKey("event"));
			Debug.Log(res["event"]);
			return res.ContainsKey("event") && (string) res["event"] == "CALL";
		}).First().Subscribe(sx =>//今回はCALLイベントしか見る気がないので一回だけ処理できればいいが、複数の相手と接続するときはFirstではまずい
		{
			//相手からCallがあったときに発火。応答処理を始める
			var response = Json.Deserialize(sx) as Dictionary<string, object>;
			var callParameters = (IDictionary) response["call_params"];
			_media_connection_id = (string) callParameters["media_connection_id"];
			//応答処理をする
			_Answer(_media_connection_id);
		}, ex => { Debug.LogError(ex); });
	}

	public RestApi(string key, string domain, string peerId, bool turn)
	{
		var peerParams = new PeerOptions();
		peerParams.key = key;
		peerParams.domain = domain;
		peerParams.peer_id = peerId;
		peerParams.turn = turn;
		string peerParamsJson = JsonUtility.ToJson(peerParams);
		byte[] peerParamsBytes = Encoding.UTF8.GetBytes(peerParamsJson);
		//SkyWayサーバとの接続開始するためのAPIを叩く
		ObservableWWW.Post(entryPoint + "/peers", peerParamsBytes).Subscribe(x =>
		{
			//この時点ではSkyWay WebRTC GWが「このPeer IDで処理を開始する」という応答でしかなく、
			//SkyWayサーバで利用できるPeer IDとは限らない(重複で弾かれる等があり得るので)
			var response = Json.Deserialize(x) as Dictionary<string, object>;
			var parameters = (IDictionary) response["params"];
			var peer_id = (string) parameters["peer_id"];
			var token = (string) parameters["token"];
			//SkyWayサーバとSkyWay WebRTC Gatewayが繋がって初めてPeer ID等が正式に決定するので、
			//イベントを監視する
			var url = string.Format("{0}/peers/{1}/events?token={2}", entryPoint, peer_id, token);
			ObservableWWW.Get(url).Repeat().Where(wx =>
			{
				//この時点ではOPENイベント以外はいらないので弾く
				var res = Json.Deserialize(wx) as Dictionary<string, object>;
				return res.ContainsKey("event") && (string) res["event"] == "OPEN";
			}).First().Subscribe(sx => //ここでは最初の一回しか監視しない。着信等のイベントは後で別の場所で取ることにする
			{
				var response_j = Json.Deserialize(sx) as Dictionary<string, object>;
				var parameters_s = (IDictionary) response_j["params"];
				//正式決定したpeer_idとtokenを記録しておく
				_peerId = (string) parameters_s["peer_id"];
				_peerToken = (string) parameters_s["token"];
				//SkyWayサーバと繋がったときの処理を始める
				_OnOpen();
			}, ex =>
			{
				//ここが発火する場合は多分peer_idやtoken等が間違っている
				//もしくはSkyWay WebRTC GWとSkyWayサーバの間で通信ができてない
				Debug.LogError(ex);
			});

		}, ex =>
		{
			//ここが発火する場合はSkyWay WebRTC GWと通信できてないのでは。
			//そもそも起動してないとか
			//他には、前回ちゃんとClose処理をしなかったため前のセッションが残っている場合が考えられる。
			//その場合はWebRTC GWを再起動するか、別のPeer IDを利用する
			//時間が経てば勝手に開放されるのでそこまで気にしなくてもよい(気にしなくてもいいとは言ってない)
			Debug.LogError("error");
			Debug.LogError(ex);
		});
	}
}

//Unityから直接叩かれるクラス
public class SkyWayRestApi : MonoBehaviour
{
	public Button ConnectButton;
	//public Button CloseButton;
	public InputField TargetIdField;
	public string key;
	public string domain;
	public string peerId;
	public bool turn;

	void Start()
	{
		//SkyWay WebRTC Gateway操作用インスタンス生成
		var _restApi = new RestApi(key, domain, peerId, turn);
	
		//ボタンとラベルは最初隠しとく
		ConnectButton.gameObject.SetActive(false);
		TargetIdField.gameObject.SetActive(false);
		//CloseButton.gameObject.SetActive(false);

		//SkyWayサーバと接続されたときに発火させるイベント
		_restApi.OnOpen += () =>
		{
			//ボタンとラベルを表示して、接続相手のPeer IDが入力されたらCallする
			ConnectButton.gameObject.SetActive(true);
			TargetIdField.gameObject.SetActive(true);
			ConnectButton.onClick.AsObservable().Select(x => TargetIdField.text).Where(x => x != "").Subscribe(x =>
			{
				_restApi.Call(x);
			});
		};

		//MediaStreamが流れ始めたときに発火させるイベント
		//正確にはSRTPパケットではなくSTUNパケットが来たときに発火するので、
		//このイベントを受けてから動画を流し始めると良い
		//相手側からのメディアはすぐ流れ始めることが予想されるので、
		//初っ端のkeyframeを受け取りそこねないように、再生待機は発信または着信時に開始したほうが良い
		_restApi.OnStream += () =>
		{
			//ボタン等はもういらないので消す
			ConnectButton.gameObject.SetActive(false);
			TargetIdField.gameObject.SetActive(false);
			ConnectButton.onClick.RemoveAllListeners();
			//切断ボタンはここから出す
			//ObservableWWWにDELETEメソッドが無いことに気づいたのでとりあえずマスクしておく
			//CloseButton.onClick.AsObservable().Subscribe(x => { _restApi.Close(); });
		};	
	}

	void Update()
	{
	}
}
