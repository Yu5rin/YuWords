# 同梱している第三者のソフトウェア

YuWords 本体は MIT ライセンスです（`LICENSE`）。配布物には、本体のほかに次のソフトウェアとデータが含まれており、それぞれのライセンスに従います。

## NMeCab（LibNMeCab 0.10.2）

- 日本語の形態素解析ライブラリ（`LibNMeCab.dll`）
- 作者: Tsuyoshi Komuta（MeCab: Taku Kudo, Nippon Telegraph and Telephone Corporation）
- ライセンス: GPL-2.0-or-later または LGPL-2.1-or-later のいずれか。YuWords は **LGPL-2.1** に従って利用しています（`licenses/LGPL-2.1.txt`）
- ソースコード: https://github.com/komutan/NMeCab
- YuWords は NMeCab を改変していません。`LibNMeCab.dll` は別のファイルとして同梱しており、同じ版の公開されているものと差し替えられます

## UniDic 2.1.2（unidic-lite 1.0.8 に同梱のもの）

- 形態素解析用の辞書（`dict/unidic/`）
- Copyright (c) 2011-2013, The UniDic Consortium
- ライセンス: GPL・LGPL・BSD のいずれか。YuWords は **BSD ライセンス**に従って利用しています（`licenses/UniDic-BSD.txt`、`dict/unidic/BSD`）
- 入手元: https://pypi.org/project/unidic-lite/ （UniDic 2.1.2 のバイナリ配布をそのまま収めたもの）

## .NET ランタイム

- 配布版は .NET 8 のランタイムを同梱しています（MIT ライセンス）。https://github.com/dotnet/runtime
