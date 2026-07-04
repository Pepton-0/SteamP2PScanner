# 自分のTODO(Codexは読まなくてよい)

overlayの根底の実装をして、codexが悩まないようにする
- Mvvmを想定して、コードビハインドはBehaviorに代替する

archiveを記憶し過ぎるとメモリ圧迫の可能性がある
圧縮したらいいんじゃね？

## 調整内容

kent2の名前はtestから除く

# ユーザーが知りたい内容

1. 相手とのping(avg, 四分位数)/packet loss
	1. SteamP2PInfoだと対戦終了後は記録が無くなってしまうが、今回ではテーブルに保存し、アプリ起動中はいつでも参照できるようにしたい
	2. ユーザーが望んだ場合キャプチャしたパケットを出力し、いつでも証拠して利用できるようにする
２. 客観的なサーバーとのping/packet lossをモニタし、1での劣悪な状態が自分によるものなのか相手によるものか区別出来るようにする

# 実装方法

WinAutoTyperが雛形になるので、それを良く熟読し、それに準拠した構造になるようにせよ
なお、IConfigServiceやILogServiceはこちらにはなく、AppConfig/GameConfigやLoggerを使用することにせよ

SpsLogic/ 実際行う内部処理の定義. ここをCodexが触る場合は基本的に開発者からの許可を得なければならない
		SpsLogic/Models/... ここについては加えて、SpsGui/Modelと同じく内部処理を統括するもので、DependencyInjectionによる起動を前提とする。
SpsGui/Models SpsLogicの各処理とやり取りを行う場所。内部処理を統括する
SpsGui/Views 表示されるGuiパーツを置く場所。Views/直下にはWindowを書き、Views/Controls/にはUserControlを書く。Data Drive的に後述のViewModelから渡された情報をGuiに表現する。Controlsでは、MVVMから若干逸脱し、Behaviorに依存せずコードビハインドを書いて構わない。
SpsGui/ViewModels は、ViewとModelの橋渡しをするViewModelを置く
SpsGui/Behaviorには、WinAutoTyperと同じくBehaviorを置く。ViewはData Driven的にしか動けないので、どうしてもコードビハインドが必要な処理はこちらにて行う。
SpsGui/Resourcesには、StaticResourceや音、絵などのリソース系を置く。i18nされうる文字列は全てこちらに置く
SpsGui/Models/Services VMとしては自分の担当するModelとViewの仲介をやりたいが、しかし他のMVVMと触れ合ってしまうのは疎結合が達成できない。よって、Serviceに依頼するものとする。

# 各種UIの説明

基本的に以下で振れられていないUIは極力追加しないこと。
余計な情報が増えるのはユーザー体験的にストレスであるため。
私からの指摘があるならば追加してよい。
各説明についてはViewとViewModel両方含んでいるし、ものによってはBehaviorで実装したほうがいいものがある。
適宜分割して実装するように。

SpsGui/View/Controls/BoxPlotControl
	テストコードのbox plotを参考に、四分位数・最小値・最大値をバインディングされた場合以下の描画を行うこと。
	特に指定がない場合白色の線により描画すること。
	数値部分とグラフ部分が上下に分かれている。数値部分について、0を左端とし、最大値を右端とする。グラフ部分についても、0に当たる部分は左端、最大値に当たる部分を右端とおく。
	あとは他パーツをテストコードと同じように描画してよい。
	背景は一切描画しないままの透明とする。
	なお、UIサイズの拡大縮小に対応して、以下を行う。
	グラフ部分の高さは固定でよい。グラフ部分の長さはUIサイズ一杯占められるように最大限大きくなれるようにする。
	数値部分について、0を左端、最大値右端とし、最小値と四分位数は中央に羅列して表示するように。Min:x/Q1:y/Med:z/Q3:w といった感じ。
	フォントサイズはUIの高さに合わせて可変である。
	数値部分とグラフ部分の間は空くと想定され、数値部分は上詰め、グラフ部分は下詰めで描画される。
	Med:zとグラフ部分の中央値は黄色く描画すること。

SpsGui/View/Controls/BarChartControl
	テストコードのbar viewを参考に、double配列と平均値をバインディングされた場合以下の描写を行うこと。
	特に指定がない場合白色で描画すること。
	背景は一切描写しないままの透明とする。
	横軸は0から配列の最大値まで。
	横軸の線の描画は0と最大値、平均値。平均値だけは緑色。
	0と最大値に限りグラフの右隣のそれぞれ右下と右上に数値を描画する。
	0未満のpingはパケロスと認識し、赤い棒を描画すること。
	Controlのサイズに合わせてUIの各パーツが拡大縮小できるようにすること。単なるアップスケーリングでは不自然なので、パーツ単位で自然な形に他のパーツと干渉しないよう調整すること。

SpsGui/Views/CoreWindow
- タイトルバー
	- 左端にアプリ名とバージョンを記入。
	- 右端からは簡易的なコマンドボタン/テキストを羅列させる場所。以下の項目に分かれる。
		- プロセス名を表示して置く場所。
- 初期画面(指定タイミングでのみ表示。別のUserControlにて定義)
	- 上半分(レイアウト的にはこれだが、実際のサイズはもっと小さくなるはず): AppConfig.SteamExeを設定できる項目。アプリ起動時にしかSteamExeは使用されない(アプリ起動中の他のタイミングでは変更できない)ので、この時点でのみ設定できれば良い。
	- 下半分のうち左側: 
		- マニュアルでSteamアプリを検知するボタン。
		- 押されたとき、SteamAppFinder.EnumWindowsの一覧を元にWindowSelectDialogを開く。
		- 何かを選択されたら、GameConfig.RegisteredGamesにてSteamAppIdがすでに登録されているか確認し、されていなかったらSteamAppIdDialogを開く。
		- されていたらプロファイリング画面へ移行する。
		- SteamAppIdDialogでidが認識出来たら、GameConfig.RegisteredGamesにパスとidを登録してプロファイリング画面へ移行する。
	- 下半分のうち右側: 
		- steam app idを所有するアプリを自動検知する欄。上側に一覧、下側にボタンを配置する。
		- 一覧では、SteamAppFinder.GetSteamProcesses()で取得されたウィンドウのタイトルを表示する。1秒ごとにGetSteamProcess()を呼び更新する。
		- 一覧のテキストは選択可能であり、以前選択したものはhwndが同一なら次の更新でも選択状態を維持する。
		- 一覧に出てきた項目が1つだけなら、自動でそれを選択する。
		- 一覧で選択されたものがある時、それをボタンの内容に反映させる…つまるところMonitor XXXなどのようにする。
		- 一覧で選択されたものがない時、ボタンはNothing detectedとなり、操作不可となる。
		- ボタンが押されたとき、GameConfig.RegisteredGamesにてパスとidが未登録なら登録して、プロファイリング画面へ移行する。
		- SteamAppInfo.IsVisible==falseのものは、自動検出の一覧に表示しないようにする。
- ProfileSreen: プロファイリング画面(指定タイミングでのみ表示。別のUserControlにて定義, SteamAppInfo/SteamPeerManager/IPacketScanをconstructorの引数として持つ)
	- 緩衝用のObservableObjectであるPingProfileSnapshotを作成する
		- PingOverlayTest.xaml.cs.PingProfileSnapshotと同じ。
	- ObservableCollection<PingProfileSnapshot>を2つ用意する。つまり現在のpingモニタリング状況と、過去のpingモニタリング状況の2つを所有する。
	- 約1秒ごとに以下の内容を呼び出す
		- PacketScan.Update()
		- PacketScan.ForEachActiveHistory()
			- 前の呼び出しと比較して0から1以上になったとき、DnsPingも同時に起動する。
				- Dnsサービスは、AppConfig.DnsIpに基づく。DnsIpはユーザーが自由に変えられるものではあるが、いまのところは堅牢なチェックはせず絶対的な信頼をしてよい。IsNullOrEmptyぐらいにとどめる。
			- 前の呼び出しと比較して1以上から0になったとき、DnsPingがあるならそれを終了する。
			- 現在のpingモニタリング状況をクリアし、これに更新する。 
			- DnsPingがあるなら、現在のpingモニタリング状況にそのping情報を追加する。
		- PacketScan.TakeUnseenOldHistories()
			- 過去のpingモニタリング状況へ挿入する。 
    - タブによって以下を表示切替できる
		- コンフィグ:
			- AppConfigとGameConfigの各種内容について、起動時の処理順序の0.2.に限り変更できるようにする。また、AppConfigのsteam exe のパスと、gameconfigのregisretedgamesはここでいじる対象ではない。
			- 名前と内容の2列に分かれ、名前を0列の左詰めにし、内容を1列の右詰めにする。
			- 名前は変数名直接ではなく、i18nされうる専用の物を設定すること。
			- boolはトグルボタンで表現する。
		- 現在のpingモニタ
			- 現在のpingモニタリング状況のテーブルを表示する。
			- 表示列は左から順に以下の通り。文字は基本的に白色とする。列のタイトルも付ける。
				- name, avg(緑色。整数), loss%(赤色。少数第一位まで), usingRelay(セルについて、ない場合は非表示だが間隔は確保してく), usingDns(セルについて、ない場合は非表示だが間隔は確保してく), BoxPlotControl, BarChartControl
		- 過去のpingモニタ
			- 過去のpingモニタリング状況のテーブルを表示する。
			- 表示列は以下の通り。文字は基本的に白色とする。列のタイトルも付ける。
				- startedAt, アーカイブ出力ボタン, name, steamid, avg(緑色。整数), loss%(赤色。少数第一位まで), BoxPlotControl, BarChartControl, usingRelay(ない場合は非表示、ただ間隔は確保してく)
					- アーカイブ出力ボタンを押すと、snapshot.PacketArchiveが呼ばれ、MetroWindowのダイアログでファイルパスとコピーボタン、OKボタンが表示される。コピーはそのままクリップボードへファイルパスを保存する。どちらのボタンを押してもダイアログは閉じる。

SpsGui/Views/WindowSelectDialog : MetroWindow
	SteamP2PInfoのWindowSelectDialogを参考にしてよい。

SpsGui/Views/SteamAppIdDialog: MetroWindow
	Steam app idの入力の旨のテキストと、実際のテキスト入力欄、テキスト入力がフォーマットに従わない時に表示するための警告テキスト、OKボタン。
	あとはWindowSelectDialogと変わらない。

SpsGui/Views/OverlayWindow

# 起動時の処理順序

0. 各種staticなクラス
	1. Logger: ログ出力は全てこちらを中継するように。
	   デバッグ用にのみ通知してほしい内容や、恐らく高頻度になり普段から読むのは邪魔になりそうなものはLogger.DebugLogを活用せよ
	   Logger.Log(msg,true)は、ユーザーにも見て欲しいものにのみ使ってよい。例えば、アプリが不具合などで中断する可能性のある処理に対し、アプリ側が認知していることを通知するため。そのログを元にユーザーが私にバグ報告を行うことを想定している
	   Logger.DebugLog(msg,true)はデバッグ用コンパイルかつファイル出力を態々行う特殊なシーンのため一応残しているが、ひょっとしたら使わないかもしれない
	2. AppConfig/GameConfig。用途はその中身やTestコードから推察せよ。Newtonsoft.Jsonでシリアライズされうる項目が、実際にUI上でも表示して変更可能にしてよい項目である。
1. App起動
	1. DependencyInjectionのセットアップ
	2. ConductorではStringResourcesを、WinAutoTyperのConductorがやっているようにローカルごとに読み込んでみる
	   無いならスキップすることで、App.xamlに指定されたデフォルトのen-usが読み込まれる
	   en-usには単なるi18nだけでなく、絵などのリソースを登録してキーから参照できるようにしてよい
	   UIに共通するデザインや色合いもこちらに登録してよい
2. CoreWindow初期画面表示
3. CoreWindowプロファイリング画面表示が要求されたとき、それを表示
	1. CoreWindowのRightWindowCommandsにて、プロセス名の項目を更新する。