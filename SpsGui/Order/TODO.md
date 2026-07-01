# 自分のTODO

SteamPeer*の実装
overlayの根底の実装をして、codexが悩まないようにする
steam app の検知を自動化する

# ユーザーが知りたい内容

1. 相手とのping(avg, 四分位数)/packet loss
	1. SteamP2PInfoだと対戦終了後は記録が無くなってしまうが、今回ではテーブルに保存し、アプリ起動中はいつでも参照できるようにしたい
	2. ユーザーが望んだ場合キャプチャしたパケットを出力し、いつでも証拠して利用できるようにする
２. 客観的なサーバーとのping/packet lossをし、1での劣悪な状態が自分によるものなのか相手によるものか区別出来るようにする

# 実装方法

WinAutoTyperが雛形になるので、それを良く熟読し、それに準拠した構造になるようにせよ

SpsLogic/ 実際行う内部処理の定義. ここをCodexが触る場合は基本的に開発者からの許可を得なければならない
SpsGui/Model SpsLogicの各処理とやり取りを行う場所。内部処理を統括する
SpsGui/Views 表示されるGuiパーツを置く場所。Views/直下にはWindowを書き、Views/Controls/にはUserControlを書く。Data Drive的に後述のViewModelから渡された情報をGuiに表現する
SpsGui/ViewModels は、ViewとModelの橋渡しをするViewModelを置く
SpsGui/Behaviorには、WinAutoTyperと同じくBehaviorを置く。ViewはData Driven的にしか動けないので、どうしてもコードビハインドが必要な処理はこちらにて行う。
SpsGui/Resourcesには、StaticResourceや音、絵などのリソース系を置く。i18nされうる文字列は全てこちらに置く

# 各種UIの説明

SpsGui/Views/CoreWindow
- タイトルバー左端にアプリ名とバージョンを記入。
  右端からは簡易的なコマンドボタン/テキストを羅列させ、うち一つはSteamアプリの自動認識をしているか、もう完了しているか、それとも失敗したか


起動時の処理順序
0. 各種staticなクラス
	1. Logger: ログ出力は全てこちらを中継するように。
	   デバッグ用にのみ通知してほしい内容や、恐らく高頻度になり普段から読むのは邪魔になりそうなものはLogger.DebugLogを活用せよ
	   Logger.Log(msg,true)は、ユーザーにも見て欲しいものにのみ使ってよい。例えば、アプリが不具合などで中断する可能性のある処理に対し、アプリ側が認知していることを通知するため。そのログを元にユーザーが私にバグ報告を行うことを想定している
	   Logger.DebugLog(msg,true)はデバッグ用コンパイルかつファイル出力を態々行う特殊なシーンのため一応残しているが、ひょっとしたら使わないかもしれない
1. App起動
	1. DependencyInjectionのセットアップ
	2. StringResourcesを、WinAutoTyperのConductorがやっているようにローカルごとに読み込んでみる
	   無いならスキップすることで、App.xamlに指定されたデフォルトのen-usが読み込まれる
	   en-usには単なるi18nだけでなく、絵などのリソースを登録してキーから参照できるようにしてよい
       UIに共通するデザインや色合いもこちらに登録してよい
2. 