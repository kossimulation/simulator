function generate_code(model)
%GENERATE_CODE Generate C++ for a Simulink model and copy it into the native core.
%   generate_code('heli_rotor') builds the model with Embedded Coder and
%   unpacks all required sources/headers (via packNGo) into
%   native/generated/heli_rotor/. Then build the core with
%   cmake -DSIMCORE_WITH_GENERATED=ON.

root = fileparts(fileparts(mfilename('fullpath')));    % .../matlab
repo = fileparts(root);
work = fullfile(tempdir, ['simcore_codegen_' model]);
target = fullfile(repo, 'native', 'generated', model);

if ~exist(work, 'dir'), mkdir(work); end
cfg = Simulink.fileGenControl('getConfig');
restore = onCleanup(@() Simulink.fileGenControl('set', ...
    'CacheFolder', cfg.CacheFolder, 'CodeGenFolder', cfg.CodeGenFolder));
Simulink.fileGenControl('set', 'CacheFolder', work, 'CodeGenFolder', work, ...
    'createDir', true);

configure_codegen(model);
slbuild(model);

% packNGo collects every file the generated code needs, incl. shared utils.
info = load(fullfile(work, [model '_ert_rtw'], 'buildInfo.mat'));
zipFile = fullfile(work, [model '.zip']);
packNGo(info.buildInfo, 'packType', 'flat', 'fileName', zipFile);

if exist(target, 'dir'), rmdir(target, 's'); end
mkdir(target);
unzip(zipFile, target);

% Drop files that must not be compiled into the library.
delete(fullfile(target, 'ert_main.*'));
delete(fullfile(target, '*.mat'));

fprintf('Generated code for %s copied to %s\n', model, target);
end
