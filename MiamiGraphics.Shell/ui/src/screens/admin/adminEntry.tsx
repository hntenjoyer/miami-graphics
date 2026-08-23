import { AdminAuthGate } from '@/screens/admin/AdminAuthGate';
import { AdminPanelScreen } from '@/screens/admin/AdminPanelScreen';

export default function AdminEntry() {
  return (
    <AdminAuthGate>
      <AdminPanelScreen />
    </AdminAuthGate>
  );
}
