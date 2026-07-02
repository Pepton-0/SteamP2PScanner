# 自分のTODO

overlayの根底の実装をして、codexが悩まないようにする
Iocにて、引数指定を伴うインスタンス作成はどうするんだ?

- AppConfigなどで現在のsteam appのパスとIDを保存できる場所を作る

# ユーザーが知りたい内容

1. 相手とのping(avg, 四分位数)/packet loss
	1. SteamP2PInfoだと対戦終了後は記録が無くなってしまうが、今回ではテーブルに保存し、アプリ起動中はいつでも参照できるようにしたい
	2. ユーザーが望んだ場合キャプチャしたパケットを出力し、いつでも証拠して利用できるようにする
２. 客観的なサーバーとのping/packet lossをし、1での劣悪な状態が自分によるものなのか相手によるものか区別出来るようにする

# 実装方法

WinAutoTyperが雛形になるので、それを良く熟読し、それに準拠した構造になるようにせよ
なお、IConfigServiceやILogServiceはこちらにはなく、AppConfigやLoggerを使用することにせよ

SpsLogic/ 実際行う内部処理の定義. ここをCodexが触る場合は基本的に開発者からの許可を得なければならない
SpsGui/Model SpsLogicの各処理とやり取りを行う場所。内部処理を統括する
SpsGui/Views 表示されるGuiパーツを置く場所。Views/直下にはWindowを書き、Views/Controls/にはUserControlを書く。Data Drive的に後述のViewModelから渡された情報をGuiに表現する
SpsGui/ViewModels は、ViewとModelの橋渡しをするViewModelを置く
SpsGui/Behaviorには、WinAutoTyperと同じくBehaviorを置く。ViewはData Driven的にしか動けないので、どうしてもコードビハインドが必要な処理はこちらにて行う。
SpsGui/Resourcesには、StaticResourceや音、絵などのリソース系を置く。i18nされうる文字列は全てこちらに置く

# 各種UIの説明

SpsGui/Views/CoreWindow
- タイトルバー
	- 左端にアプリ名とバージョンを記入。
	- 右端からは簡易的なコマンドボタン/テキストを羅列させる場所。今のところは適当なテキストを複数並べておけば問題ない。
- 初期画面(指定タイミングでのみ表示。別のUserControlにて定義)
	- AppConfig.SteamExeを設定できる項目。アプリ起動時にしかSteamExeは使用されない(アプリ起動中の他のタイミングでは変更できない)ので、この時点でのみ設定できれば良い。
	- Steamアプリ検知ボタン。初期画面の下側に配置して、初期画面の他の設定項目が完了したのちに触るものであることを分かりやすくする。これが入力されたとき、具体的な実装は後回しにしてとりあえず初期画面の代わりに次のプロファイリング画面を引数armoredcore6と1888160と共に呼んで表示せよ。
- プロファイリング画面(指定タイミングでのみ表示。別のUserControlにて定義)
    - タブによって以下を表示切替できる
		- 現在のping
		- 過去のping
		- コンフィグ(exclude appconfig.steamexe)

SpsGui/Views/OverlayWindow

# 起動時の処理順序

1. 0. 各種staticなクラス
	1. Logger: ログ出力は全てこちらを中継するように。
	   デバッグ用にのみ通知してほしい内容や、恐らく高頻度になり普段から読むのは邪魔になりそうなものはLogger.DebugLogを活用せよ
	   Logger.Log(msg,true)は、ユーザーにも見て欲しいものにのみ使ってよい。例えば、アプリが不具合などで中断する可能性のある処理に対し、アプリ側が認知していることを通知するため。そのログを元にユーザーが私にバグ報告を行うことを想定している
	   Logger.DebugLog(msg,true)はデバッグ用コンパイルかつファイル出力を態々行う特殊なシーンのため一応残しているが、ひょっとしたら使わないかもしれない
2. App起動
	1. DependencyInjectionのセットアップ
	2. StringResourcesを、WinAutoTyperのConductorがやっているようにローカルごとに読み込んでみる
	   無いならスキップすることで、App.xamlに指定されたデフォルトのen-usが読み込まれる
	   en-usには単なるi18nだけでなく、絵などのリソースを登録してキーから参照できるようにしてよい
	   UIに共通するデザインや色合いもこちらに登録してよい
3. CoreWindow初期画面表示
4. CoreWindowプロファイリング画面表示が要求されたとき、それを表示
	1.