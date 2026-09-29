import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Modal } from '../ui/Modal';
import { Input } from '../ui/Input';
import { Button } from '../ui/Button';
import { useToast } from '../ui/Toast';
import { useAuth } from '../../hooks/useAuth';
import { Lock, KeyRound, Check } from 'lucide-react';

export interface ChangePasswordModalProps {
  isOpen: boolean;
  onClose: () => void;
}

export const ChangePasswordModal: React.FC<ChangePasswordModalProps> = ({ isOpen, onClose }) => {
  const { changePassword } = useAuth();
  const { toast } = useToast();
  const navigate = useNavigate();

  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);

  const handleReset = () => {
    setCurrentPassword('');
    setNewPassword('');
    setConfirmPassword('');
    setErrorMsg(null);
    setIsSubmitting(false);
  };

  const handleClose = () => {
    handleReset();
    onClose();
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErrorMsg(null);

    if (!currentPassword || !newPassword || !confirmPassword) {
      setErrorMsg('All password fields are required');
      return;
    }

    if (newPassword !== confirmPassword) {
      setErrorMsg('New password and confirmation password do not match');
      return;
    }

    if (newPassword.length < 12) {
      setErrorMsg('New password must be at least 12 characters long');
      return;
    }

    setIsSubmitting(true);

    try {
      await changePassword({ currentPassword, newPassword });
      toast({
        title: 'Password Changed',
        description: 'Session ended following security stamp rotation. Please log in with your new password.',
        variant: 'success',
        duration: 5000,
      });
      handleClose();
      navigate('/login');
    } catch (err: any) {
      let message = 'Failed to change password. Verify your current password.';
      if (err?.data?.errors && Array.isArray(err.data.errors)) {
        message = err.data.errors.join('; ');
      } else if (err?.message) {
        message = err.message;
      }
      setErrorMsg(message);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title={
        <div className="flex items-center gap-2">
          <KeyRound className="w-5 h-5 text-kestrel-accent" />
          <span>Change Account Password</span>
        </div>
      }
      subtitle="Updating password will end your active session across all devices."
      footer={
        <>
          <Button variant="secondary" size="sm" onClick={handleClose} disabled={isSubmitting}>
            Cancel
          </Button>
          <Button
            variant="primary"
            size="sm"
            onClick={handleSubmit}
            isLoading={isSubmitting}
            leftIcon={<Check className="w-4 h-4" />}
          >
            {isSubmitting ? 'Updating...' : 'Update Password'}
          </Button>
        </>
      }
    >
      <form onSubmit={handleSubmit} className="space-y-4">
        {errorMsg && (
          <div className="p-3 bg-rose-950/60 border border-rose-500/40 rounded text-xs text-rose-300 font-medium">
            {errorMsg}
          </div>
        )}

        <Input
          label="Current Password"
          type="password"
          value={currentPassword}
          onChange={(e) => setCurrentPassword(e.target.value)}
          placeholder="Enter current password"
          leftIcon={<Lock className="w-4 h-4" />}
          required
          autoComplete="current-password"
        />

        <Input
          label="New Password"
          type="password"
          value={newPassword}
          onChange={(e) => setNewPassword(e.target.value)}
          placeholder="At least 12 chars, upper, lower, digit, symbol"
          helperText="Must meet minimum complexity requirements (12+ chars)"
          leftIcon={<KeyRound className="w-4 h-4" />}
          required
          autoComplete="new-password"
        />

        <Input
          label="Confirm New Password"
          type="password"
          value={confirmPassword}
          onChange={(e) => setConfirmPassword(e.target.value)}
          placeholder="Re-enter new password"
          leftIcon={<KeyRound className="w-4 h-4" />}
          required
          autoComplete="new-password"
        />
      </form>
    </Modal>
  );
};
