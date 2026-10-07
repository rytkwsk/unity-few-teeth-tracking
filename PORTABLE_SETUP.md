# 別PCでのセットアップ

## 推奨ディレクトリ構成

3つのリポジトリを同じ親ディレクトリへ配置します。

```text
Projects/
  unity-few-teeth-tracking/
  few_teeth_tracking/
  pym3t/
```

Unityの各シーンは、Unityプロジェクトを基準に `../few_teeth_tracking` を参照します。
Pythonスクリプトは自身のリポジトリを基準に `../pym3t` を参照します。そのため、
ユーザー名やドライブ文字が変わっても設定変更は不要です。

## Python環境

既定では、Unityは次の順にCondaを探します。

1. `M3T_CONDA_BAT` 環境変数
2. Unity Inspectorの `condaBat`
3. `%USERPROFILE%/anaconda3/condabin/conda.bat`
4. `%USERPROFILE%/miniconda3/condabin/conda.bat`
5. PATH上の `conda.bat`

環境名の既定値は `pym3t` です。別名の場合は `M3T_CONDA_ENV` を設定します。
Condaを使わない場合はInspectorで `useCondaActivate` を無効にし、
`M3T_PYTHON_EXE` または `pythonExe` にPython実行ファイルを指定します。

## 異なる配置を使う場合

- `PYM3T_DIR`: Pythonスクリプトから参照するpym3tディレクトリ
- `M3T_SOURCE_PROJECT_DIR`: 実験スクリプトから参照するfew_teeth_trackingディレクトリ
- `M3T_SOURCE_MODEL_DIR`: 実験スクリプトから参照するモデルディレクトリ
- `M3T_CONDA_BAT`: `conda.bat` の絶対パス
- `M3T_CONDA_ENV`: Conda環境名
- `M3T_PYTHON_EXE`: Condaを使わない場合のPython実行ファイル

個別PCの絶対パスはUnityシーンやPythonソースへコミットせず、これらの環境変数で指定します。
