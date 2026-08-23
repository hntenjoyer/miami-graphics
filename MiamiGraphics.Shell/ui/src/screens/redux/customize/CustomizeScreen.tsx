import { useCustomizeStore } from '@/store/customizeStore';
import { ManageScreen } from './ManageScreen';
import { GenericComponentScreen } from './GenericComponentScreen';
import { MinimapScreen } from './MinimapScreen';
import { TracersScreen } from './TracersScreen';
import { ImportReduxPicker } from './ImportReduxPicker';
import { ImportReduxPreview } from './ImportReduxPreview';
import { ArmorLibraryPreview } from './ArmorLibraryPreview';
import { LibraryPicker } from './LibraryPicker';
import { BigMapPickerScreen } from './BigMapPickerScreen';

export function CustomizeScreen() {
  const view = useCustomizeStore(s => s.view);

  if (view.kind === 'bigmap-picker') {
    return <BigMapPickerScreen />;
  }

  if (view.kind === 'import-picker') {
    return <ImportReduxPicker forComponent={view.forComponent} />;
  }

  if (view.kind === 'import-preview') {
    return <ImportReduxPreview forComponent={view.forComponent} donorReduxId={view.donorReduxId} />;
  }

  if (view.kind === 'armor-library-preview') {
    return <ArmorLibraryPreview
      armorLibraryId={view.armorLibraryId}
      armorLibraryName={view.armorLibraryName}
      previewUrl={view.previewUrl}
      glbUrl={view.glbUrl}
    />;
  }

  if (view.kind === 'library-picker') {
    return <LibraryPicker forComponent={view.forComponent} />;
  }

  if (view.kind === 'component') {
    if (view.component === 'minimap') return <MinimapScreen />;
    if (view.component === 'tracers') return <TracersScreen />;
    return <GenericComponentScreen component={view.component} />;
  }

  return <ManageScreen />;
}
